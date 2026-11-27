#!/usr/bin/env bash
set -u
ulimit -c 0
mkdir -p /home/data/translator-bootstrap
export GATEWAY_VERIFY_ENDPOINT="$1"
mode="${2:-}"
if [[ "$mode" == "--departments" ]]; then
	guard=gateway-departments-once
	probe=/home/site/wwwroot/gateway-probe/verify-ai-gateway.dll
else
	guard=gateway-dotnet-once
	probe=/home/data/translator-bootstrap/gateway-probe/verify-ai-gateway.dll
fi
if mkdir "/home/data/translator-bootstrap/$guard" 2>/dev/null; then
	dotnet "$probe" "$mode" > /home/data/translator-bootstrap/gateway-verify.log 2>&1
	echo "VERIFY exit=$?" >> /home/data/translator-bootstrap/gateway-verify.log
fi
cd /home/site/wwwroot
exec dotnet /home/site/wwwroot/chatui.dll