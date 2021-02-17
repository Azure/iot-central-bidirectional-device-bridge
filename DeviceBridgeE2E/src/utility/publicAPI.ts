import { ExecutionContext } from 'ava';
import got, { Response } from 'got';

export default class PublicAPI {
    static async create(
        appUrl: string,
        apiToken: string
    ): Promise<PublicAPI> {
        return new PublicAPI(appUrl, apiToken);
    }

    private _appUrl: string;
    private _apiToken: string;

    private constructor(appUrl: string, apiToken: string) {
        this._appUrl = appUrl;
        this._apiToken = apiToken;
    }

    async createDeviceTemplate(
        t: ExecutionContext,
        template: DeviceTemplate
    ): Promise<Required<DeviceTemplate>> {
        return this._retry(async () => {
            const { body } = await got.put<Required<DeviceTemplate>>(
                this._url(`/deviceTemplates/${template.id}`),
                {
                    json: template,
                    responseType: 'json',
                    headers: await this._headers(),
                    hooks: {
                        afterResponse: [this._logger(t, 'PUT')],
                    },
                }
            );

            return body;
        });
    }

    async createDevice(
        t: ExecutionContext,
        device: Device
    ): Promise<Required<Device>> {
        return this._retry(async () => {
            const { body } = await got.put<Required<Device>>(
                this._url(`/devices/${device.id}`),
                {
                    json: device,
                    responseType: 'json',
                    headers: await this._headers(),
                    hooks: {
                        afterResponse: [this._logger(t, 'PUT')],
                    },
                }
            );

            return body;
        });
    }

    async getDeviceCredentials(
        t: ExecutionContext,
        id: string
    ): Promise<DeviceCredentials> {
        return this._retry(async () => {
            const { body } = await got<DeviceCredentials>(
                this._url(`/devices/${id}/credentials`),
                {
                    responseType: 'json',
                    headers: await this._headers(),
                    hooks: {
                        afterResponse: [this._logger(t, 'GET')],
                    },
                }
            );

            return body;
        });
    }

    async getLatestDeviceTelemetry(
        t: ExecutionContext,
        id: string,
        telemetryName: string
    ): Promise<{ [name: string]: any }> {
        return this._retry(async () => {
            const { body } = await got.get<{ [name: string]: string }>(
                this._url(`/devices/${id}/telemetry/${telemetryName}`),
                {
                    responseType: 'json',
                    headers: await this._headers(),
                    hooks: {
                        afterResponse: [this._logger(t, 'GET')],
                    },
                }
            );

            return body;
        });
    }

    async getProperties(
        t: ExecutionContext,
        id: string
    ): Promise<{ [name: string]: any }> {
        return this._retry(async () => {
            const { body } = await got.get<{ [name: string]: string }>(
                this._url(`/devices/${id}/properties`),
                {
                    responseType: 'json',
                    headers: await this._headers(),
                    hooks: {
                        afterResponse: [this._logger(t, 'GET')],
                    },
                }
            );

            return body;
        });
    }

    async executeCommand(
        t: ExecutionContext,
        id: string,
        commandName: any
    ): Promise<{ [name: string]: any }> {
        return this._retry(async () => {
            const { body } = await got.post<{ [name: string]: string }>(
                this._url(`/devices/${id}/commands/${commandName}`),
                {
                    json: {
                        connectionTimeout: 10,
                        responseTimeout: 10,
                        request: {
                        },
                    },
                    responseType: 'json',
                    headers: await this._headers(),
                    hooks: {
                        afterResponse: [this._logger(t, 'POST')],
                    },
                }
            );

            return body;
        });
    }

    private _url(path: string): string {
        return `https://${this._appUrl}/api/preview${path}`;
    }

    private async _headers(): Promise<{ [name: string]: string }> {
        return {
            Authorization: `${this._apiToken}`,
        };
    }

    private async _retry<T>(fn: () => Promise<T>): Promise<T> {
        // Flat 10 second delay for any 404 error code (application still provisioning).
        let lastErr: any;
        for (let i = 0; i < 10; i += 1) {
            try {
                return await fn();
            } catch (err) {
                lastErr = err;
                if (!err || !err.response) {
                    throw err;
                }

                const response: Response = err.response;
                if (response.statusCode !== 404) {
                    throw err;
                }

                // Retry
            }

            await new Promise(resolve => setTimeout(resolve, 10000));
        }

        return lastErr;
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

export interface Device {
    id: string;
    etag?: string;
    displayName: string;
    instanceOf: string;
    simulated: boolean;
    approved?: boolean;
    provisioned?: boolean;
}

export interface DeviceCredentials {
    idScope: string;
    symmetricKey: {
        primaryKey: string;
        secondaryKey: string;
    };
}

export interface DeviceTemplate {
    id: string;
    etag?: string;
    types: string[];
    displayName: string;
    description?: string;
    capabilityModel: any;
    solutionModel: any;
}
