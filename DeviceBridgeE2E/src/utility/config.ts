import { loadSync } from '@azure-iot/config';

export default loadSync({
    defaultValues: {
        RESOURCE_GROUP: 'IOTC',
    },
});
