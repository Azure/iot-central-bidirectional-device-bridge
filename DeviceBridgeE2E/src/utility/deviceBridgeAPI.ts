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
        await got.post<any>(this.getUrlFuncs()["telemetry"](deviceId), {
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
        await got.patch<any>(this.getUrlFuncs()["reportedProperties"](deviceId), {
            json: propertiesBody,
            responseType: 'json',
            headers: await this._headers(),
            hooks: {
                afterResponse: [this._logger(t, 'PATCH')],
            },
        });
    }

    async getTwin(t: ExecutionContext, deviceId: string): Promise<any> {
        const { body } = await got<any>(this.getUrlFuncs()["twin"](deviceId), {
            responseType: 'json',
            headers: await this._headers(),
            hooks: {
                afterResponse: [this._logger(t, 'GET')],
            },
        });
        return body;
    }

    async getHealth(t: ExecutionContext): Promise<any> {
        const { body } = await got<any>(this.getHealthUrl(), {
            headers: await this._headers(),
            hooks: {
                afterResponse: [this._logger(t, 'GET')],
            },
        });
        return body;
    }

    async createC2DSubscription(
        t: ExecutionContext,
        deviceId: string,
        callbackUrl: string
    ): Promise<any> {
        await got.put<any>(this.getUrlFuncs()["C2DMessage"](deviceId), {
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
        await got.put<any>(this.getUrlFuncs()["cmd"](deviceId), {
            json: {callbackUrl},
            responseType: 'json',
            headers: await this._headers(),
            hooks: {
                afterResponse: [this._logger(t, 'PUT')],
            },
        });
    }

    async createDesiredPropertySubscription(
        t: ExecutionContext,
        deviceId: string,
        callbackUrl: string
    ): Promise<any> {
        await got.put<any>(this.getUrlFuncs()["desiredProperties"](deviceId), {
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
        await got.put<any>(this.getUrlFuncs()["connectionStatus"](deviceId) + "/sub", {
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
        return await got<any>(this.getUrlFuncs()["C2DMessage"](deviceId), {
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
        return await got<any>(this.getUrlFuncs()["cmd"](deviceId), {
            responseType: 'json',
            headers: await this._headers(),
            hooks: {
                afterResponse: [this._logger(t, 'GET')],
            },
        });
    }

    async getDesiredPropertySubscription(
        t: ExecutionContext,
        deviceId: string
    ): Promise<any> {
        return await got<any>(this.getUrlFuncs()["desiredProperties"](deviceId), {
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
        return await got.delete<any>(this.getUrlFuncs()["cmd"](deviceId), {
            headers: await this._headers(),
            hooks: {
                afterResponse: [this._logger(t, 'DELETE')],
            },
        });
    }

    async deleteConnectionStatusSubscription(
        t: ExecutionContext,
        deviceId: string
    ): Promise<any> {
        return await got.delete<any>(this.getUrlFuncs()["connectionStatus"](deviceId) + "/sub", {
            headers: await this._headers(),
            hooks: {
                afterResponse: [this._logger(t, 'DELETE')],
            },
        });
    }

    async deleteC2DSubscription(
        t: ExecutionContext,
        deviceId: string
    ): Promise<any> {
        return await got.delete<any>(this.getUrlFuncs()["C2DMessage"](deviceId), {
            headers: await this._headers(),
            hooks: {
                afterResponse: [this._logger(t, 'DELETE')],
            },
        });
    }

    async deleteDesiredPropertySubscription(
        t: ExecutionContext,
        deviceId: string
    ): Promise<any> {
        return await got.delete<any>(this.getUrlFuncs()["desiredProperties"](deviceId), {
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
        return await got<any>(this.getUrlFuncs()["connectionStatus"](deviceId) + "/sub", {
            responseType: 'json',
            headers: await this._headers(),
            hooks: {
                afterResponse: [this._logger(t, 'GET')],
            },
        });
    }

    async getConnectionStatus(
        t: ExecutionContext,
        deviceId: string
    ): Promise<any> {
        return await got<any>(this.getUrlFuncs()["connectionStatus"](deviceId), {
            responseType: 'json',
            headers: await this._headers(),
            hooks: {
                afterResponse: [this._logger(t, 'GET')],
            },
        });
    }


    async registerDevice(
        t: ExecutionContext,
        deviceId: string,
        body: any,
    ): Promise<any> {
        return await got.post<any>(this.getUrlFuncs()["registration"](deviceId), {
            json: body,
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
        return await got<any>(this.getEchoUrl(deviceId), {
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
        return await got.delete<any>(this.getEchoUrl(deviceId), {
            responseType: 'json',
            headers: await this._headers(),
            hooks: {
                afterResponse: [this._logger(t, 'DELETE')],
            },
        });
    }

    private getEchoUrl(deviceId: string): string {
        return `${this._azureFunctionUrl}&deviceId=${deviceId}`;
    }

    private getHealthUrl(): string {
        return `https://${this._bridgeURL}/health`;
    }

    getUrlFuncs():object {
        return {
            registration: (deviceId) => `https://${this._bridgeURL}/devices/${deviceId}/registration`,
            telemetry: (deviceId) => `https://${this._bridgeURL}/devices/${deviceId}/messages/events`,
            C2DMessage: (deviceId) => `https://${this._bridgeURL}/devices/${deviceId}/devicebound/sub`,
            cmd: (deviceId) => `https://${this._bridgeURL}/devices/${deviceId}/methods/sub`,
            desiredProperties: (deviceId) => `https://${this._bridgeURL}/devices/${deviceId}/twin/properties/desired/sub`,
            connectionStatus: (deviceId) => `https://${this._bridgeURL}/devices/${deviceId}/connectionstatus`,
            reportedProperties: (deviceId) => `https://${this._bridgeURL}/devices/${deviceId}/twin/properties/reported`,
            twin: (deviceId) => `https://${this._bridgeURL}/devices/${deviceId}/twin`
        };
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
