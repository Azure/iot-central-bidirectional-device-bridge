import { ExecutionContext } from 'ava';
import got, { Response } from 'got';

export default class DeviceBridgeAPI {
    static async create(
        bridgeURL: string,
        apiKey: string
    ): Promise<DeviceBridgeAPI> {
        return new DeviceBridgeAPI(bridgeURL, apiKey);
    }

    private _bridgeURL: string;
    private _apiKey: string;

    private constructor(bridgeURL: string, apiKey: string) {
        this._bridgeURL = bridgeURL;
        this._apiKey = apiKey;
    }

    async sendTelemetry(
        t: ExecutionContext,
        deviceId: string,
        telemetry: any
    ): Promise<any> {
        const { body } = await got.post<any>(this._telemetryURL(deviceId), {
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

    private _telemetryURL(deviceId: string): string {
        return `https://${this._bridgeURL}/devices/${deviceId}/messages/events`;
    }

    private _reportedPropertiesURL(deviceId: string): string {
        return `https://${this._bridgeURL}/devices/${deviceId}/twin/properties/reported`;
    }

    private _getTwinURL(deviceId: string): string {
        return `https://${this._bridgeURL}/devices/${deviceId}/twin`;
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
