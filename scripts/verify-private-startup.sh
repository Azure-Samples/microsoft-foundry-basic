#!/usr/bin/env bash
set -u
ulimit -c 0
cd /home/site/wwwroot
echo 'VERIFY starting' > /home/data/translator-bootstrap/verify.log
dotnet ./chatui.dll --verify-services >> /home/data/translator-bootstrap/verify.log 2>&1
echo "VERIFY exit=$?" >> /home/data/translator-bootstrap/verify.log
exec dotnet ./chatui.dll