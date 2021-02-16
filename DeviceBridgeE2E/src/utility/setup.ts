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
    callbackUrl: string
}

export async function setup(t: ExecutionContext): Promise<TestContext> {
    var publicAPI = await PublicAPI.create(config.getRequired("APP_URL"), config.getRequired("BEARER_TOKEN"));
    var deviceBridgAPI = await DeviceBridgeAPI.create(config.getRequired("DEVICE_BRIDGE_URL"), config.getRequired("DEVICE_BRIDGE_KEY"), config.getRequired("AZURE_FUNCTION_URL"));
    var callbackUrl = config.getRequired("AZURE_FUNCTION_URL") as string;

    return {
        publicAPI,
        deviceBridgAPI,
        callbackUrl
    };
}
