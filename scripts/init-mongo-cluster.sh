#!/usr/bin/env bash
# Initializes the sharded MongoDB cluster brought up by docker-compose.yml: one config-server
# replset, two shard replsets, one mongos router - then shards wishlist_read on _id (hashed),
# since userId is a high-cardinality opaque identifier with no natural range-locality the way
# Product's category.id has (database-design.md). Run this once, after `docker compose up -d` and
# before the API starts consuming Mongo. Safe to re-run (rs.initiate/sh.addShard/sh.shardCollection
# are idempotent - Mongo returns an "already initialized"/"already sharded" error this script
# tolerates), mirroring kart-product-service's/kart-cart-service's own init script precedent.
set -euo pipefail

wait_for_mongo() {
  local container=$1
  local port=$2
  echo "Waiting for $container:$port ..."
  for _ in $(seq 1 30); do
    if docker exec "$container" mongosh --quiet --port "$port" --eval "db.runCommand('ping')" >/dev/null 2>&1; then
      echo "$container:$port is up"
      return 0
    fi
    sleep 2
  done
  echo "Timed out waiting for $container:$port" >&2
  exit 1
}

wait_for_mongo kart-wishlist-mongo-configsvr 27019
wait_for_mongo kart-wishlist-mongo-shard1 27018
wait_for_mongo kart-wishlist-mongo-shard2 27018

echo "Initiating config server replica set..."
docker exec kart-wishlist-mongo-configsvr mongosh --quiet --port 27019 --eval '
  try {
    rs.initiate({ _id: "wishlistCfgRS", configsvr: true, members: [{ _id: 0, host: "kart-wishlist-mongo-configsvr:27019" }] });
  } catch (e) {
    if (!String(e).includes("already initialized")) { throw e; }
  }
'

echo "Initiating shard 1 replica set..."
docker exec kart-wishlist-mongo-shard1 mongosh --quiet --port 27018 --eval '
  try {
    rs.initiate({ _id: "wishlistShard1RS", members: [{ _id: 0, host: "kart-wishlist-mongo-shard1:27018" }] });
  } catch (e) {
    if (!String(e).includes("already initialized")) { throw e; }
  }
'

echo "Initiating shard 2 replica set..."
docker exec kart-wishlist-mongo-shard2 mongosh --quiet --port 27018 --eval '
  try {
    rs.initiate({ _id: "wishlistShard2RS", members: [{ _id: 0, host: "kart-wishlist-mongo-shard2:27018" }] });
  } catch (e) {
    if (!String(e).includes("already initialized")) { throw e; }
  }
'

echo "Waiting for replica sets to elect a primary..."
sleep 10

echo "Waiting for mongos router..."
wait_for_mongo kart-wishlist-mongo-router 27017

echo "Adding shards to the cluster..."
docker exec kart-wishlist-mongo-router mongosh --quiet --port 27017 --eval '
  try { sh.addShard("wishlistShard1RS/kart-wishlist-mongo-shard1:27018"); } catch (e) { if (!String(e).includes("duplicate")) { print(e); } }
  try { sh.addShard("wishlistShard2RS/kart-wishlist-mongo-shard2:27018"); } catch (e) { if (!String(e).includes("duplicate")) { print(e); } }
'

echo "Enabling sharding on the kart_wishlist database and sharding wishlist_read on _id (hashed)..."
docker exec kart-wishlist-mongo-router mongosh --quiet --port 27017 --eval '
  sh.enableSharding("kart_wishlist");
  sh.shardCollection("kart_wishlist.wishlist_read", { _id: "hashed" });
'

echo "Sharding status:"
docker exec kart-wishlist-mongo-router mongosh --quiet --port 27017 --eval 'sh.status()'

echo "Mongo cluster initialized."
