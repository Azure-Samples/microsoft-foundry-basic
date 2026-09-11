#!/bin/bash
set -euo pipefail

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
for var in BASE_NAME LOCATION RESOURCE_GROUP SUBSCRIPTION_ID TENANT_ID; do
  if [[ -z "${!var:-}" ]]; then
    echo "ERROR: Required variable $var is not set in $CONFIG_FILE"
    exit 1
  fi
done

ACTUAL_TENANT=$(az account show --subscription "$SUBSCRIPTION_ID" --query tenantId -o tsv | tr -d '\r')
if [[ "$ACTUAL_TENANT" != "$TENANT_ID" ]]; then
  echo "ERROR: Subscription tenant does not match TENANT_ID. No resources were changed."
  exit 1
fi

echo "Deploying with configuration:"
echo "  SUBSCRIPTION_ID=$SUBSCRIPTION_ID"
echo "  TENANT_ID=$TENANT_ID"
echo "  BASE_NAME=$BASE_NAME"
echo "  LOCATION=$LOCATION"
echo "  RESOURCE_GROUP=$RESOURCE_GROUP"
echo ""

GROUP_LOCATION=$(az group show --subscription "$SUBSCRIPTION_ID" --name "$RESOURCE_GROUP" --query location -o tsv | tr -d '\r')
if [[ "$GROUP_LOCATION" != "$LOCATION" ]]; then
  echo "ERROR: Existing resource group location does not match LOCATION. No resources were changed."
  exit 1
fi


PRINCIPAL_ID=$(az rest --subscription "$SUBSCRIPTION_ID" --url 'https://graph.microsoft.com/v1.0/me' --query id -o tsv | tr -d '\r')
PRINCIPAL_NAME=$(az rest --subscription "$SUBSCRIPTION_ID" --url 'https://graph.microsoft.com/v1.0/me' --query userPrincipalName -o tsv | tr -d '\r')

# Optional Entra ID app registration client ID for App Service Easy Auth v2.
# Leave empty on first deploy; set once the app registration exists and redeploy.
ENTRA_APP_CLIENT_ID="${ENTRA_APP_CLIENT_ID:-}"

# This takes about 15-20 minutes to run.
DEPLOY_ARGS=(
  --subscription "$SUBSCRIPTION_ID"
  --template-file "$SCRIPT_DIR/../infra-as-code/bicep/main.bicep"
  --resource-group "$RESOURCE_GROUP"
  --parameters "baseName=$BASE_NAME" "location=$LOCATION"
  "yourPrincipalId=$PRINCIPAL_ID" "yourPrincipalDisplayName=$PRINCIPAL_NAME"
  "entraAppClientId=$ENTRA_APP_CLIENT_ID"
)
az deployment group what-if "${DEPLOY_ARGS[@]}"
az deployment group create "${DEPLOY_ARGS[@]}"

# Post-deploy hints are printed as the `postDeployHint*` outputs by main.bicep:
#   - Seed the meeting join-token signing key into Key Vault.
#   - Run `CREATE EXTENSION vector;` in the Postgres app database with your Entra token.
#   - Then run EF Core migrations from the app to create the workload schema.