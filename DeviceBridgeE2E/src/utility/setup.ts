import { ExecutionContext } from 'ava';
import PublicAPI from './publicAPI';
import DeviceBridgeAPI from './deviceBridgeAPI';

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
    var args = process.argv.slice(2);
    var APP_URL = args[0];
    var DEVICE_BRIDGE_URL = args[1];
    var DEVICE_BRIDGE_KEY = args[2];
    var AZURE_FUNCTION_URL = args[3];
    var BEARER_TOKEN = args[4];

    var publicAPI = await PublicAPI.create(APP_URL,BEARER_TOKEN);
    var deviceBridgAPI = await DeviceBridgeAPI.create(DEVICE_BRIDGE_URL, DEVICE_BRIDGE_KEY, AZURE_FUNCTION_URL);
    var callbackUrl = AZURE_FUNCTION_URL;
    
    return {
        publicAPI,
        deviceBridgAPI,
        callbackUrl
    };
}
