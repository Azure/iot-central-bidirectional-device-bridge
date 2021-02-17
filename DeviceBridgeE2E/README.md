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
```

