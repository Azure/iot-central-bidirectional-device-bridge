import { ExecutionContext } from 'ava';
import PublicAPI from './publicAPI';
import DeviceBridgeAPI from './deviceBridgeAPI';
import config from './config';

export interface ServicePrincipal {
    clientId: string;
    tenantId: string;
    password: string;
}

export interface TestContext {
    publicAPI: PublicAPI;
    deviceBridgAPI: DeviceBridgeAPI;
}

export async function setup(t: ExecutionContext): Promise<TestContext> {
    var publicAPI = await PublicAPI.create(config.getRequired("SUB_DOMAIN"), config.getRequired("BASE_DOMAIN"), config.getRequired("BEARER_TOKEN"));
    var deviceBridgAPI = await DeviceBridgeAPI.create(config.getRequired("DEVICE_BRIDGE_URL"), config.getRequired("DEVICE_BRIDGE_KEY"));

    return {
        publicAPI,
        deviceBridgAPI
    };
}
