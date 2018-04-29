#!/bin/sh
set -x -e

HOST="mysqldemodb001.mysql.database.azure.com"
USER="yoichika@mysqldemodb001"

# 20 thread 1000row 1000 query read(table scan)
mysqlslap \
 --no-defaults --auto-generate-sql --engine=innodb --auto-generate-sql-add-autoincrement \
 --host=$HOST --port=3306 -u $USER -p \
 --number-int-cols=10 \
 --number-char-cols=10 \
 --iterations=10 \
 --concurrency=20 \
 --auto-generate-sql-write-number=1000 \
 --number-of-queries=1000 \
 --auto-generate-sql-load-type=read
