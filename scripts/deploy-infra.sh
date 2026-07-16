#!/bin/bash
set -e

# Load configuration from deploy-infra.local.env
# See deploy-infra.env for template and documentation
SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
CONFIG_FILE="${SCRIPT_DIR}/deploy-infra.local.env"

if [[ ! -f "$CONFIG_FILE" ]]; then
  echo "ERROR: Configuration file not found: $CONFIG_FILE"
  echo "Copy deploy-infra.env to deploy-infra.local.env and update with your values:"
  echo "  cp $SCRIPT_DIR/deploy-infra.env $CONFIG_FILE"
  exit 1
fi

source "$CONFIG_FILE"

# Validate required variables
for var in BASE_NAME LOCATION RESOURCE_GROUP; do
  if [[ -z "${!var}" ]]; then
    echo "ERROR: Required variable $var is not set in $CONFIG_FILE"
    exit 1
  fi
done

echo "Deploying with configuration:"
echo "  BASE_NAME=$BASE_NAME"
echo "  LOCATION=$LOCATION"
echo "  RESOURCE_GROUP=$RESOURCE_GROUP"
echo ""

az group create -l $LOCATION -n $RESOURCE_GROUP --tags "SECURITYCONTROL=IGNORE" "Environment=dev"


PRINCIPAL_ID=$(az ad signed-in-user show --query id -o tsv | tr -d '\r')

# This takes about 10 minutes to run.
az deployment group create -f ./infra-as-code/bicep/main.bicep \
  -g $RESOURCE_GROUP \
  -p baseName=${BASE_NAME} \
  -p yourPrincipalId=$PRINCIPAL_ID