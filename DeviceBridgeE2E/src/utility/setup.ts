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
    var publicAPI = await PublicAPI.create("test", "test", /*config.getRequired("APP_URL"), config.getRequired("BEARER_TOKEN")*/);
    var deviceBridgAPI = await DeviceBridgeAPI.create("test", "test", "test",/*config.getRequired("DEVICE_BRIDGE_URL"), config.getRequired("DEVICE_BRIDGE_KEY"), config.getRequired("AZURE_FUNCTION_URL")*/);
    var callbackUrl = "test"; //config.getRequired("AZURE_FUNCTION_URL") as string;

    return {
        publicAPI,
        deviceBridgAPI,
        callbackUrl
    };
}
