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
    var APP_URL = "";
    var DEVICE_BRIDGE_URL = "";
    var DEVICE_BRIDGE_KEY = "";
    var AZURE_FUNCTION_URL = "";
    var BEARER_TOKEN = "";

    args.forEach(arg => {
        if(arg.startsWith("--app-url=")){
            APP_URL = arg.substring(arg.indexOf('=')+1).trim();
        }
        if(arg.startsWith("--device-bridge-url=")){
            DEVICE_BRIDGE_URL = arg.substring(arg.indexOf('=')+1).trim();
        }
        if(arg.startsWith("--device-bridge-key=")){
            DEVICE_BRIDGE_KEY = arg.substring(arg.indexOf('=')+1).trim();
        }
        if(arg.startsWith("--azure-function-url=")){
            AZURE_FUNCTION_URL = arg.substring(arg.indexOf('=')+1).trim();
        }
        if(arg.startsWith("--bearer-token=")){
            BEARER_TOKEN = arg.substring(arg.indexOf('=')+1).trim();
        }
    })

    var publicAPI = await PublicAPI.create(APP_URL,BEARER_TOKEN);
    var deviceBridgAPI = await DeviceBridgeAPI.create(DEVICE_BRIDGE_URL, DEVICE_BRIDGE_KEY, AZURE_FUNCTION_URL);
    var callbackUrl = AZURE_FUNCTION_URL;
    
    return {
        publicAPI,
        deviceBridgAPI,
        callbackUrl
    };
}
