import * as fs from 'fs';
import * as util from 'util';

import chalk from 'chalk';
import execa from 'execa';
import meow from 'meow';

async function main() {
    const cli = meow(
        `
        Usage
          $ npm run generate-config -- --bridge-url --bridge-key --app-url

        Options
          --bridge-url, -b                    URL of the device bridge
          --bridge-key, -k                API key for bridge
          --app-url, -a                   URL of IOTC app
    `,
        {
            flags: {
                bridgeUrl: {
                    type: 'string',
                    alias: 'b',
                },
                bridgeKey: {
                    type: 'string',
                    alias: 'k',
                },
                appUrl: {
                    type: 'string',
                    alias: 'a',
                },
            },
        }
    );

    if (!cli.flags.bridgeUrl || !cli.flags.bridgeKey || !cli.flags.appUrl) {
        throw new Error('Missing parameters: bridge-url, bridge-key and app-url must be provided.');
    }

    const output = await execCLI('az', [
        'account',
        'get-access-token',
        '--resource',
        "https://apps.azureiotcentral.com"
    ]);

    const cliResult = JSON.parse(output);


    await util.promisify(fs.writeFile)(
        'user-config.json',
        JSON.stringify(
            {
                "DEVICE_BRIDGE_URL": cli.flags.bridgeUrl,
                "DEVICE_BRIDGE_KEY": cli.flags.bridgeKey, 
                "APP_URL": cli.flags.appUrl,
                "BEARER_TOKEN": cliResult.accessToken
            },
            null,
            4
        ),
        'utf-8'
    );

    console.log('Generated configuration in user-config.json');
}

main().catch(err => {
    console.log(err);
    process.exit(1);
});

async function execCLI(command: string, args: string[]): Promise<string> {
    const cmd = [command, ...args]
        .map(arg =>
            /\s/.test(arg) ? `"${arg.replace(/["\\]/g, '\\$0')}"` : arg
        )
        .join(' ');
    console.log(chalk.bold(`${chalk.magenta('>')} ${cmd}`));

    const proc = execa(command, args);

    proc.stderr?.pipe(process.stderr);

    const { stdout } = await proc;
    return stdout;
}
