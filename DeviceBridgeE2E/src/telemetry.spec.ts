import anyTest, { TestInterface } from 'ava';
import { v4 } from 'uuid';
import {  DeviceCredentials, DeviceTemplate } from './utility/publicAPI';
import { setup, TestContext } from './utility/setup';
import * as util from 'util';
import * as fs from 'fs';
import DeviceClient from './utility/device';
import { assert } from 'console';

const test = anyTest as TestInterface<{
    ctx: TestContext;
    device: DeviceClient;
    template: DeviceTemplate;
}>;

test.before(async t => {
    t.context.ctx = await setup(t);
   
    var deviceTemplate = JSON.parse(
        await util.promisify(fs.readFile)(
            './data/deviceTemplates/basic.json',
            'utf-8'
        )
    );

    await t.context.ctx.publicAPI.createDeviceTemplate(t, deviceTemplate);

    var device = await t.context.ctx.publicAPI.createDevice(t, {
        id: v4(),
        displayName: 'Basic Telemetry Device',
        instanceOf: deviceTemplate.id,
        simulated: false,
    })

    let creds: DeviceCredentials | undefined;
    for (let i = 0; i < 5; i += 1) {
        creds = await t.context.ctx.publicAPI.getDeviceCredentials(
            t,
            device.id
        );
        if (creds.symmetricKey) {
            // Sometimes the credentials have no symmetric key. This is
            // generally just a race condition.
            t.log(`Received device credentials for device ${device.id}`);
            break;
        }

        t.log(
            `Received device credentials response with no symmetric key. Retrying...`
        );
        await new Promise(resolve => setTimeout(resolve, 5000));
    }

    if (!creds || !creds.symmetricKey) {
        t.fail('invalid credentials');
        return;
    }

    t.context.device = await DeviceClient.create(
        device.id,
        creds.idScope,
        creds.symmetricKey.primaryKey
    );
});

test.serial('Test device to cloud messaging', async t => {
    const temperatureValue = 120;
    await t.context.ctx.deviceBridgAPI.sendTelemetry(t, t.context.device.id, {data: {"temperature": temperatureValue}})
    
    var attempts = 5;
    var telemetryResult = await t.context.ctx.publicAPI.getLatestDeviceTelemetry(t, t.context.device.id, "temperature");
    // Multiple attempts as it may take time for telemetry to come through
    for(var i = 0; i < attempts; i++){
        if(telemetryResult != {}){
            break;
        }
        telemetryResult = await t.context.ctx.publicAPI.getLatestDeviceTelemetry(t, t.context.device.id, "temperature");
    }
    t.assert(temperatureValue, String(telemetryResult.value));
});

test.serial('Test reported properties and twin', async t => {
    var reportedPropertiesBody = {
        "patch": {
            "rwProp": "testValue",
        }
    }

    await t.context.ctx.deviceBridgAPI.sendReportedProperty(t, t.context.device.id, reportedPropertiesBody)
    var attempts = 5;
    var propertiesResult = await t.context.ctx.publicAPI.getProperties(t, t.context.device.id);
    // Multiple attempts as it may take time for property changes to come through
    for(var i = 0; i < attempts; i++){
        if(propertiesResult != {}){
            break;
        }
        propertiesResult = await t.context.ctx.publicAPI.getProperties(t, t.context.device.id);
    }

    t.assert(reportedPropertiesBody.patch.rwProp, String(propertiesResult.rwProp));

    // Assert that twin reports the property
    var twinResult = await t.context.ctx.deviceBridgAPI.getTwin(t, t.context.device.id)
    assert(reportedPropertiesBody.patch.rwProp, twinResult.twin.properties.reported.rwProp)
});