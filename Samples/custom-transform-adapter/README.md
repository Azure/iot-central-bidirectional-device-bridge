
# JSON to JSON telemetry transformation - parametrized adapter
This adapter can be used to extend the Device Bridge API (both parameters and data format) without the need to write custom code.
The code, written in Go, is deployed as a side-car container alongside the Bridge and is configured through a route definition file.
The configuration can specify a custom [jq](https://stedolan.github.io/jq/) query to transform telemetry messages.
All telemetry messages received by the adapter are transformed and forwarded to the Bridge.

- [JSON to JSON telemetry transformation - parametrized adapter](#json-to-json-telemetry-transformation---parametrized-adapter)
  * [Deployment](#deployment)
    + [API surface](#api-surface)
    + [Uploading configuration file](#uploading-configuration-file)
    + [Logs](#logs)
  * [Configuration](#configuration)
    + [Route parameters](#route-parameters)
      - [`path`](#-path-)
      - [`transform`](#-transform-)
      - [`deviceIdPathParam`](#-deviceidpathparam-)
      - [`deviceIdBodyQuery`](#-deviceidbodyquery-)
      - [`authHeader`](#-authheader-)
      - [`authQueryParam`](#-authqueryparam-)
    + [Example](#example)
## Deployment
To deploy, build the image in this directory and push to your registry. Then use the template below to deploy the solution. This template is a
simple extension of the one found in the root of this repository (a complete definition of the parameters can be found [here](https://github.com/iot-for-all/iotc-device-bridge#3---deployment-parameters).
This will deploy the main Bridge container as well as the adapter container (the parameter `adapter-image` is the name of the adapter image to pull from the registry).

[![Deploy to Azure](http://azuredeploy.net/deploybutton.png)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fiot-for-all%2Fiotc-device-bridge%2Fmain%2FSamples%2Fcustom-transform-adapter%2Fazuredeploy.json)

### API surface
The deployment will forward all requests to `/devices/*` and `/health` directly to the Bridge. All other requests will be routed to the adapter for transformation.

### Uploading configuration file
A storage account is provisioned with every instance of the Device Bridge. This account will be in the same resource group and contains a File Share named `bridge`.
By default, the adapter will look for a `config.json` configuration file in the `bridge` File Share. The example command below uploads a configuration file to the Bridge
storage account. The format of the configuration file is explained in the next sections.
 
`az storage file upload --account-name <account-name> --path ./config.json --share-name bridge --source <path-to-local-config-file>`

Once the configuration file has been uploaded, restart the Bridge instance so the new configuration can be applied. The container logs will display which routes are being configured.

### Logs
The adapter logs will be published to the same Log Analytics Workspace and the Bridge.

## Configuration
A configuration file must be in JSON format and have the format below. Each entry of the `d2cMessages` array specifies
a route that will receive `POST` requests with telemetry messages.

```
{
    "d2cMessages": [
      {
        // Parameters for route 1
      },
      {
        // Parameters for route 2
      },
      // Other routes
    ]
}
```

### Route parameters
The following route configuration parameters are available:

#### `path`
Path filter for requests that will be handled by this route. For instance, `"path": "/message"` will handle all `POST` requests made to `/message`.
A path definition can have parameters. For instance `"path": "/telemetry/{id}"` defined a path parameter `id` and will handle any requests that
start with `/telemetry/`.

#### `transform`
[jq](https://stedolan.github.io/jq/) query that defines how request bodies received by this route will be transformed before being forwarded to
the Bridge. Transformations take the request body as input and must output a JSON object that meets the the Device Bridge
[telemetry body format](https://github.com/iot-for-all/iotc-device-bridge#device-to-cloud-messages).

For instance, the transformation `"transform": "{ data: . }"` will convert the following request body:

```json
{
    "temperature": 21
}
```

into:

```json
{
  "data": {
    "temperature": 21
  }
}
```

Which is a valid telemetry body format for the Bridge.

> NOTE: if a transform is not specified, the route will pass the request body to the Device Bridge _as is_.

#### `transformFile`
Similar to `tranform`, but specifies the path to the file that contains the jq query. The query file must placed in the same location as
the `config.json`, in the `bridge` File Share of the Storage Account provisioned with the Bridge.

#### `deviceIdPathParam`
Specifies the name of the path parameter the will contain the device Id. For instance, if we have a route with `"path": "/telemetry/{id}"`
and a `"deviceIdPathParam": "id"`, a `POST` request to `/telemetry/my-device` will result in the telemetry being sent on behalf of device `my-device`.

#### `deviceIdBodyQuery`
Defines a jq query that will be used to pick the device Id from the request body. This query must generate a string as output.
For instance, the query `"deviceIdBodyQuery": ".device.id"` will pick the `my-device` Id from the following request body:

```json
{
  "device": {
    "id": "my-device"
  }
}
```

#### `authHeader`
Specifies the name of the custom header that will contain the API key used to authenticate with the Device Bridge (specified during deployment).

#### `authQueryParam`
Name of the query parameter that contains the Device Bridge API key for authentication.

### Example
The following example demonstrates the configuration parameters above and how they affect the behavior of each route:

```json
{
    "d2cMessages": [{
        "path": "/model1/telemetry",
        "transform": "{ data: .reports | map( { (.name | tostring): .value } ) | add, properties: { seq_id: .originator.seq_id | tostring }, componentName: .originator.board_model, creationTimeUtc: .originator.time }",
        "deviceIdBodyQuery": ".originator.hw_serial | tostring",
        "authQueryParam": "key"
    },
    {
        "path": "/{device_id}/telemetry",
        "deviceIdPathParam": "device_id",
        "authHeader": "Api-Key"
    }]
}
```

The first route will convert the following HTTP request:

```json
POST /mode1/telemetry?key=<my-api-key>
{
    "reports": [
        {
            "name": "temperature",
            "value": 21
        },
        {
            "name": "humidity",
            "value": 34.2
        }
    ],
    "originator": {
        "hw_serial": 218009,
        "board_model": "Smart_Sensor",
        "seq_id": 117340065,
        "time": "2019-09-22T12:42:31Z"
    }
}
```

Into the following telemetry request to the Device Bridge:

```json
x-api-key: <my-api-key>
POST /devices/218009/messages/events
{
    "data": {
        "temperature": 21,
        "humidity": 34.2
    },
    "properties": {
        "seq_id": "117340065"
    },
    "componentName": "Smart_Sensor",
    "creationTimeUtc": "2019-09-22T12:42:31Z"
}
```

The second route, will convert the following request:

```json
Api-Key: <my-api-key>
POST /my-device-1/telemetry
{
    "data": {
        "speed": 14.2
    }
}
```

Into the following request to the Device Bridge (since the transform is not defined, the route passes the body _as is_):

```json
x-api-key: <my-api-key>
POST /devices/my-device-1/messages/events
{
    "data": {
        "speed": 14.2
    }
}
```