# Device Bridge E2E Tests

Test suite which tests end to end functionality for Device Bridge.

## Setup

To run the setup, first make sure you have set up this project by running:

```
npm ci
```

Before running tests, the following must be deployed:

- Azure IoT Central App
    - App URL required
    - Api token required
- Device Bridge deployed
    - URL required
- Device Bridge E2E Echo app deployed
    - URL required with key

## Execution

To run the tests, use the following command:

``` 
npm run start -- --
  --app-url=<iot-central-app-url> 
  --device-bridge-key=<key> 
  --azure-function-url=<url>?=<key>
  --device-bridge-url=<url> 
  --api-token=<iot-central-api-token>
  --restart-api-url={azure REST api to restart containers}
  --restart-bearer-token={bearer token for azure APIs}
```

Example restart api url: https://management.azure.com/subscriptions/{subscription id}/resourceGroups/device-bridge-deployment/providers/Microsoft.ContainerInstance/containerGroups/{container groups name}/restart?api-version=2019-12-01

Powershell script to get azure bearer token:
```
$azContext = Get-AzContext
$azProfile = [Microsoft.Azure.Commands.Common.Authentication.Abstractions.AzureRmProfileProvider]::Instance.Profile
$profileClient = New-Object -TypeName Microsoft.Azure.Commands.ResourceManager.Common.RMProfileClient -ArgumentList ($azProfile)
$token = $profileClient.AcquireAccessToken($azContext.Subscription.TenantId)
```