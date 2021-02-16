import { ExecutionContext } from 'ava';
import got, { Response } from 'got';

export default class DeviceBridgeAPI {
    static async create(
        bridgeURL: string,
        apiKey: string,
        azureFunctionUrl: string
    ): Promise<DeviceBridgeAPI> {
        return new DeviceBridgeAPI(bridgeURL, apiKey, azureFunctionUrl);
    }

    private _bridgeURL: string;
    private _apiKey: string;
    private _azureFunctionUrl;

    private constructor(bridgeURL: string, apiKey: string, azureFunctionUrl: string) {
        this._bridgeURL = bridgeURL;
        this._apiKey = apiKey;
        this._azureFunctionUrl = azureFunctionUrl;
    }

    async sendTelemetry(
        t: ExecutionContext,
        deviceId: string,
        telemetry: any
    ): Promise<any> {
        await got.post<any>(this._telemetryURL(deviceId), {
            json: telemetry,
            responseType: 'json',
            headers: await this._headers(),
            hooks: {
                afterResponse: [this._logger(t, 'POST')],
            },
        });
    }

    async sendReportedProperty(
        t: ExecutionContext,
        deviceId: string,
        propertiesBody: any
    ): Promise<any> {
        await got.patch<any>(this._reportedPropertiesURL(deviceId), {
            json: propertiesBody,
            responseType: 'json',
            headers: await this._headers(),
            hooks: {
                afterResponse: [this._logger(t, 'PATCH')],
            },
        });
    }

    async getTwin(t: ExecutionContext, deviceId: string): Promise<any> {
        const { body } = await got<any>(this._getTwinURL(deviceId), {
            responseType: 'json',
            headers: await this._headers(),
            hooks: {
                afterResponse: [this._logger(t, 'PATCH')],
            },
        });
        return body;
    }

    async createC2DSubscription(
        t: ExecutionContext,
        deviceId: string,
        callbackUrl: string
    ): Promise<any> {
        await got.put<any>(this._C2DMessageURL(deviceId), {
            json: {callbackUrl},
            responseType: 'json',
            headers: await this._headers(),
            hooks: {
                afterResponse: [this._logger(t, 'PUT')],
            },
        });
    }

    async createCMDSubscription(
        t: ExecutionContext,
        deviceId: string,
        callbackUrl: string
    ): Promise<any> {
        await got.put<any>(this._CMDMessageURL(deviceId), {
            json: {callbackUrl},
            responseType: 'json',
            headers: await this._headers(),
            hooks: {
                afterResponse: [this._logger(t, 'PUT')],
            },
        });
    }

    async createConnectionStatusSubscription(
        t: ExecutionContext,
        deviceId: string,
        callbackUrl: string
    ): Promise<any> {
        await got.put<any>(this._ConnectionStatusURL(deviceId), {
            json: {callbackUrl},
            responseType: 'json',
            headers: await this._headers(),
            hooks: {
                afterResponse: [this._logger(t, 'PUT')],
            },
        });
    }

    async getC2DSubscription(
        t: ExecutionContext,
        deviceId: string
    ): Promise<any> {
        return await got<any>(this._C2DMessageURL(deviceId), {
            responseType: 'json',
            headers: await this._headers(),
            hooks: {
                afterResponse: [this._logger(t, 'GET')],
            },
        });
    }

    async getCMDSubscription(
        t: ExecutionContext,
        deviceId: string
    ): Promise<any> {
        return await got<any>(this._CMDMessageURL(deviceId), {
            responseType: 'json',
            headers: await this._headers(),
            hooks: {
                afterResponse: [this._logger(t, 'GET')],
            },
        });
    }

    async deleteCMDSubscription(
        t: ExecutionContext,
        deviceId: string
    ): Promise<any> {
        return await got.delete<any>(this._CMDMessageURL(deviceId), {
            headers: await this._headers(),
            hooks: {
                afterResponse: [this._logger(t, 'DELETE')],
            },
        });
    }

    async getConnectionStatusSubscription(
        t: ExecutionContext,
        deviceId: string
    ): Promise<any> {
        return await got<any>(this._ConnectionStatusURL(deviceId), {
            responseType: 'json',
            headers: await this._headers(),
            hooks: {
                afterResponse: [this._logger(t, 'GET')],
            },
        });
    }

    async getEcho(
        t: ExecutionContext,
        deviceId: string
    ): Promise<any> {
        return await got<any>(this._echoUrl(deviceId), {
            responseType: 'json',
            headers: await this._headers(),
            hooks: {
                afterResponse: [this._logger(t, 'GET')],
            },
        });
    }

    async deleteEcho(
        t: ExecutionContext,
        deviceId: string
    ): Promise<any> {
        return await got.delete<any>(this._echoUrl(deviceId), {
            responseType: 'json',
            headers: await this._headers(),
            hooks: {
                afterResponse: [this._logger(t, 'DELETE')],
            },
        });
    }

    
    private _echoUrl(deviceId: string): string {
        return `${this._azureFunctionUrl}?deviceId=${deviceId}`;
    }

    private _telemetryURL(deviceId: string): string {
        return `http://${this._bridgeURL}/devices/${deviceId}/messages/events`;
    }

    private _C2DMessageURL(deviceId: string): string {
        return `http://${this._bridgeURL}/devices/${deviceId}/devicebound/sub`;
    }

    private _CMDMessageURL(deviceId: string): string {
        return `http://${this._bridgeURL}/devices/${deviceId}/methods/sub`;
    }

    private _ConnectionStatusURL(deviceId: string): string {
        return `http://${this._bridgeURL}/devices/${deviceId}/connectionstatus/sub`;
    }

    private _reportedPropertiesURL(deviceId: string): string {
        return `http://${this._bridgeURL}/devices/${deviceId}/twin/properties/reported`;
    }

    private _getTwinURL(deviceId: string): string {
        return `http://${this._bridgeURL}/devices/${deviceId}/twin`;
    }

    private async _headers(): Promise<{ [name: string]: string }> {
        return {
            'x-api-key': this._apiKey,
        };
    }

    private _logger<T>(
        t: ExecutionContext,
        method: string
    ): (response: Response<T>) => Response<T> {
        return response => {
            t.log(
                `${method} ${response.requestUrl} - ${response.statusCode} (${response.timings.phases.total}ms)`
            );
            if (response.statusCode >= 400) {
                t.log(response.body);
            }
            return response;
        };
    }
}
