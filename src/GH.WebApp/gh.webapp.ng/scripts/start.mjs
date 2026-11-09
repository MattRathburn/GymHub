// Runs `ng serve` on the port Aspire assigns through PORT (4200 when run on its own).
import { spawn } from 'node:child_process';

const port = process.env.PORT ?? '4200';
const child = spawn('npx', ['ng', 'serve', '--port', port], { stdio: 'inherit', shell: true });

child.on('exit', (code) => process.exit(code ?? 0));
