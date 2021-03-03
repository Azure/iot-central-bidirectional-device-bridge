import { EventEmitter } from 'events';
import * as util from 'util';

import { Client, Message, Twin } from 'azure-iot-device';
import { Mqtt } from 'azure-iot-device-mqtt';
import { ProvisioningDeviceClient } from 'azure-iot-provisioning-device';
import { Http } from 'azure-iot-provisioning-device-http';
import { SymmetricKeySecurityClient } from 'azure-iot-security-symmetric-key';

const DPS_DOMAIN = 'global.azure-devices-provisioning.net';

export default class DeviceClient extends EventEmitter {
    static async create(
        deviceId: string,
        scopeId: string,
        deviceKey: string
    ): Promise<DeviceClient> {
        const dpsClient = ProvisioningDeviceClient.create(
            DPS_DOMAIN,
            scopeId,
            new Http(),
            new SymmetricKeySecurityClient(deviceId, deviceKey)
        );

        const result = await util.promisify(
            dpsClient.register.bind(dpsClient)
        )();

        const connectionString = `HostName=${result!.assignedHub};DeviceId=${
            result!.deviceId
        };SharedAccessKey=${deviceKey}`;

        const client = Client.fromConnectionString(connectionString, Mqtt);
        await util.promisify(client.open.bind(client))();

        const twin = (await util.promisify(
            client.getTwin.bind(client)
        )()) as Twin;
        return new DeviceClient(deviceId, client, twin);
    }

    readonly id: string;

    private _client: Client;
    private _twin: Twin;

    private constructor(id: string, client: Client, twin: Twin) {
        super();

        this.id = id;
        this._client = client;
        this._twin = twin;

        client.on('error', err => this.emit('error', err));
    }

    async sendMessage(body: any, properties: { [name: string]: string } = {}) {
        const message = new Message(JSON.stringify(body));

        for (const [key, value] of Object.entries(properties)) {
            message.properties.add(key, value);
        }

        await util.promisify(this._client.sendEvent.bind(this._client))(
            message
        );
    }

    async updateTwin(body: any) {
        await util.promisify(
            this._twin.properties.reported.update.bind(
                this._twin.properties.reported
            )
        )(body);
    }

    async close() {
        await util.promisify(this._client.close.bind(this._client))();
    }
}
