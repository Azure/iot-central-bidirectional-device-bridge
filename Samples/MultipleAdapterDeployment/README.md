# Multiple adapter deployment

The `deploy-multi-adapter.sh` script in this folder can be used to deploy multiple adapters to an existing
Device Bridge instance (to deploy the Bridge for the first time, use the template available in the root of the repository).
If the number of adapters is the same as the current instance, the update will be done in place (containers will be restarted with their new version). If new adapters are being added, the container group instance will be deleted and recreated with the correct configuration.

## Parameters
Before deploying, replace the parameters at the top of the script file with the appropriate values.
The script will look for an existing instance of device bridge in the provided resource group.

## Adapter configuration and request routing
Each adapter is deployed as a separate container. The webserver is configured to route external requests to
each adapter based on the first part of the request path. For instance, consider the parameters below:

```bash
adapterImages=("myacr.io/adpterimage1" "myacr.io/adpterimage2" "myacr.io/adpterimage3")
adapterPathPrefixes=("adapter1" "adapter2" "adapter3")
```

All requests whose path start with `/adapter1/` (e.g., `https://mybridge.azurecontainers.io/adapter1/message`) will be
routes to the adapter running image `myacr.io/adpterimage1`. Requests whose path starts with `/adapter2/` will
be routed to `myacr.io/adpterimage2` and so on.

## Ports and environment variables
Each adapter will be given a port for incoming external requests. It will also have an internal port
visible to the local container network, so it can receive requests from other adapters or the bridge core. These and
other parameters are passed to the container as environment variables as follows:

- `PORT`: port to which the adapter should listen for external requests. The webserver will route to this port any requests that
match the path prefix of the adapter.
- `INTERNAL_PORT`: port that is only exposed to the internal network and that can be used, for instance, to
listen for requests from the Bridge core.
- `BRIDGE_PORT`: internal port on which the Bridge core container is listening. Requests for core operations, such as sending
telemetry or subscribing to events, should be send to this port (e.g., `http://localhost:{BRIDGE_PORT}/devices/{deviceId}/messages/events`).
- `PATH_PREFIX`: the path used by the webserver to route external requests to this adapter. The adapter code should listen for
requests whose first path component equals this prefix.

The following is an example of an adapter file written in TypeScript that uses these variables:

```typescript
import express from 'express';

const port = process.env['PORT'];
const pathPrefix = process.env['PATH_PREFIX'];

const externalApp = express();
externalApp.post(`/${pathPrefix}/event`, async (req, res, next) => res.sendStatus(200));
externalApp.listen(port, () => console.log(`External server listening at http://localhost:${port}`));
```