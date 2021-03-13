#!/bin/bash

####################################################################
# Deploys multiple adapters to an existing device bridge instance. #
# Requests are routed to each adapter based on a path prefix.      #
####################################################################

resourceGroup=
acrServer=
acrUsername=
acrPassword=
logAnalyticsId=
logAnalyticsKey=
adapterImages=
adapterPathPrefixes=

# Read parameters
while :; do
    case $1 in
      --resource-group)
        resourceGroup=$2
        shift
        ;;
      --acr-server)
        acrServer=$2
        shift
        ;;
      --acr-username)
        acrUsername=$2
        shift
        ;;
      --acr-password)
        acrPassword=$2
        shift
        ;;
      --log-analytics-workspace-id)
        logAnalyticsId=$2
        shift
        ;;
      --log-analytics-workspace-key)
        logAnalyticsKey=$2
        shift
        ;;
      --adapter-images)
        adapterImages=(${2//,/ })
        shift
        ;;
      --adapter-path-prefixes)
        adapterPathPrefixes=(${2//,/ })
        shift
        ;;
      -?*)
        echo "ERROR: Unknown option $1"
        exit
        ;;
      *)
        break
    esac
    shift
done

if ([[ -z "$resourceGroup" ]] || [[ -z "$acrServer" ]] || [[ -z "$acrUsername" ]] || [[ -z "$acrPassword" ]] || [[ -z "$logAnalyticsId" ]] || [[ -z "$logAnalyticsKey" ]]); then
  echo "ERROR: All parameters must be difined"
  exit
fi

if ([[ ${#adapterImages[@]} == 0 ]] || [[ ${#adapterPathPrefixes[@]} == 0 ]] || [[ ${#adapterImages[@]} != ${#adapterPathPrefixes[@]} ]]); then
  echo "ERROR: Number of adapter images must match the number of path prefixes"
  exit
fi

# Check for existing bridge resources in the resource group (we locate the resources by name)
echo "Fetching existing Bridge resources..."

containerGroupName=$(az container list --resource-group $resourceGroup --query "[?starts_with(name, 'iotc-container-groups-')].{name:name}[?"'!'"contains(name, '-setup-')]" --output tsv)
echo "Bridge container group:" $containerGroupName

location=$(az container show --resource-group $resourceGroup --name $containerGroupName --query location --output tsv)
dnsNameLabel=$(az container show --resource-group $resourceGroup --name $containerGroupName --query 'ipAddress.dnsNameLabel' --output tsv)
bridgeContainerName=$(az container show --resource-group $resourceGroup --name $containerGroupName --query "containers[?starts_with(name, 'iotc-bridge-container-')][name]" --output tsv)
bridgeImage=$(az container show --resource-group $resourceGroup --name $containerGroupName --query "containers[?starts_with(name, 'iotc-bridge-container-')][image]" --output tsv)
keyvaultName=$(az keyvault list --resource-group $resourceGroup --query "[?starts_with(name, 'iotc-kv-')].[name]" --output tsv)
echo "Key Vault:" $keyvaultName

storageAccountName=$(az storage account list --resource-group $resourceGroup --query "[?starts_with(name, 'iotcsa')].[name]" --output tsv)
storageAccountKey=$(az storage account keys list --resource-group $resourceGroup --account-name $storageAccountName --query '[0].value' -o tsv)
echo "Storage account": $storageAccountName

# Generate Caddyfile
caddyFile="${dnsNameLabel}.${location}.azurecontainer.io

route {
"

adapterCount=${#adapterImages[@]}

for ((i=0;i<adapterCount;i++)); do
    caddyFile="${caddyFile}    reverse_proxy /${adapterPathPrefixes[i]}/* localhost:3$(printf %03d $i)
"
done

caddyFile="${caddyFile}    respond 404
}"

base64Caddyfile=$(echo "$caddyFile" | base64)

echo -e "\nCaddyfile config"
echo -e "----------------"
echo "$caddyFile"

yamlFile=$(mktemp)

# Add first part of YAML deployment file
echo "type: Microsoft.ContainerInstance/containerGroups
apiVersion: 2019-12-01
name: \"${containerGroupName}\"
location: \"${location}\"
identity:
  type: SystemAssigned
properties:
  sku: Standard
  containers:" >> "$yamlFile"

# Add the YAML deployment definition for each adapter
for ((i=0;i<adapterCount;i++)); do
    adapterDefinition="  - name: \"${adapterPathPrefixes[i]}\"
    properties:
      image: \"${adapterImages[i]}\"
      ports:
      - port: 3$(printf %03d $i)
      - port: 4$(printf %03d $i)
      environmentVariables:
      - name: PORT
        value: 3$(printf %03d $i)
      - name: INTERNAL_PORT
        value: 4$(printf %03d $i)
      - name: BRIDGE_PORT
        value: 5001
      - name: PATH_PREFIX
        value: \"${adapterPathPrefixes[i]}\"
      resources:
        requests:
          memoryInGB: 0.5
          cpu: 0.5"
    echo "${adapterDefinition}" >> "$yamlFile"
done

# Add the last part of the YAML file
echo "  - name: \"${bridgeContainerName}\"
    properties:
      image: \"${bridgeImage}\"
      ports:
      - port: 5001
      environmentVariables:
      - name: MAX_POOL_SIZE
        value: \"50\"
      - name: DEVICE_RAMPUP_BATCH_SIZE
        value: \"150\"
      - name: DEVICE_RAMPUP_BATCH_INTERVAL_MS
        value: \"1000\"
      - name: KV_URL
        value: \"https://${keyvaultName}.vault.azure.net/\"
      - name: PORT
        value: \"5001\"
      resources:
        requests:
          memoryInGB: 1.5
          cpu: 0.8
  - name: caddy-ssl-server
    properties:
      image: caddy@sha256:d0b43ebda8fd47409cec98d5f3c3b4c60bfc6bca35338313c002dc64c2283055
      command:
      - \"/bin/sh\"
      - \"-c\"
      - \"caddy run --config /mnt/caddyfile --adapter caddyfile 2>> /tmp/caddy.log\"
      ports:
      - protocol: TCP
        port: 443
      - protocol: TCP
        port: 80
      environmentVariables: []
      resources:
        requests:
          memoryInGB: 0.5
          cpu: 0.2
      volumeMounts:
      - name: data
        mountPath: \"/data\"
      - name: config
        mountPath: \"/config\"
      - name: caddyfile
        mountPath: \"/mnt\"
  volumes:
  - name: data
    azureFile:
      shareName: bridge
      storageAccountName: \"${storageAccountName}\"
      storageAccountKey: \"${storageAccountKey}\"
  - name: config
    azureFile:
      shareName: bridge
      storageAccountName: \"${storageAccountName}\"
      storageAccountKey: \"${storageAccountKey}\"
  - name: caddyfile
    secret:
      caddyfile: \"${base64Caddyfile}\"
  initContainers: []
  restartPolicy: Always
  imageRegistryCredentials:
  - server: \"${acrServer}\"
    username: \"${acrUsername}\"
    password: \"${acrPassword}\"
  diagnostics:
    logAnalytics:
      workspaceId: \"${logAnalyticsId}\"
      workspaceKey: \"${logAnalyticsKey}\"
  ipAddress:
    ports:
    - protocol: TCP
      port: 443
    type: Public
    dnsNameLabel: \"${dnsNameLabel}\"
  osType: Linux" >> "$yamlFile"

echo -e "\nYAML deplyment definition"
echo -e "-------------------------"
cat "$yamlFile"

# Deploy final YAML
echo -e "\nTrying to update containers in place..."
updateResult=$(az container create --resource-group $resourceGroup --file $yamlFile 2>&1)

# If the update changes the number of adapters, the container group needs to be deleted and recreated
if [[ $updateResult == *"BadRequestError"*"delete it first and then create a new one"* ]]; then
  echo "Updates can't be made in place. Will delete and recreate the container group"
  az container delete --resource-group $resourceGroup --name $containerGroupName
  az container create --resource-group $resourceGroup --file $yamlFile
  identity=$(az container show --resource-group $resourceGroup --name $containerGroupName --query identity.principalId --out tsv)
  az keyvault set-policy --name $keyvaultName --resource-group $resourceGroup --object-id $identity --secret-permissions get list
else
  echo "$updateResult"
  echo "Container group successfully updated"
fi

rm "$yamlFile"

