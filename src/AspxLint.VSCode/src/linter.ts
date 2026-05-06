import * as vscode from 'vscode';
import { spawn } from 'child_process';
import * as path from 'path';

export interface LintIssue {
    ruleId: string;
    ruleName: string;
    severity: 'error' | 'warning' | 'info';
    line: number;
    col: number;
    snippet: string;
    hint: string;
}

interface AnalyzeResponse {
    ext: string;
    issues: LintIssue[];
}

/**
 * Wrapper autour du binaire `aspx-lint` (CLI). Les frontends VSCode passent
 * par cette classe pour ne jamais traiter les details d'argv ou de parsing.
 *
 * Strategie : on fait `aspx-lint analyze --ext <ext> --stdin` avec le buffer
 * en cours sur stdin. Pas de fichier temp, pas de race condition avec le save.
 */
export class Linter {
    constructor(private output: vscode.OutputChannel) {}

    private resolveBinary(): string {
        const cfg = vscode.workspace.getConfiguration('aspxLint');
        return cfg.get<string>('path', 'aspx-lint');
    }

    /**
     * Analyse un buffer en memoire. Renvoie la liste d'issues (ou erreur si
     * le binaire crashe / n'est pas trouve).
     */
    async analyze(content: string, filePath: string): Promise<LintIssue[]> {
        const ext = path.extname(filePath).toLowerCase().replace('.', '') || 'aspx';
        const cwd = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath ?? path.dirname(filePath);

        const binary = this.resolveBinary();
        const args = ['analyze', '--ext', ext, '--stdin'];

        return new Promise<LintIssue[]>((resolve, reject) => {
            let proc: ReturnType<typeof spawn>;
            try {
                proc = spawn(binary, args, { cwd });
            } catch (err: any) {
                return reject(new Error(
                    `Cannot launch '${binary}'. Install with: dotnet tool install -g aspx-lint`
                ));
            }

            let stdoutBuf = '';
            let stderrBuf = '';
            proc.stdout!.on('data', (d: Buffer) => { stdoutBuf += d.toString('utf8'); });
            proc.stderr!.on('data', (d: Buffer) => { stderrBuf += d.toString('utf8'); });

            proc.on('error', err => {
                reject(new Error(
                    `aspx-lint launch failed: ${err.message}. Is the binary installed (dotnet tool install -g aspx-lint) and in PATH?`
                ));
            });

            proc.on('close', code => {
                // Exit codes : 0 = clean, 1 = issues found, 2 = error.
                if (code === 2) {
                    return reject(new Error(`aspx-lint error: ${stderrBuf.trim() || 'unknown'}`));
                }
                try {
                    const parsed = JSON.parse(stdoutBuf) as AnalyzeResponse;
                    resolve(parsed.issues ?? []);
                } catch (err: any) {
                    this.output.appendLine(`Failed to parse output: ${stdoutBuf.slice(0, 500)}`);
                    reject(new Error(`Cannot parse aspx-lint output: ${err.message}`));
                }
            });

            // Inject stdin then close.
            proc.stdin!.write(content, 'utf8');
            proc.stdin!.end();
        });
    }

    /**
     * Lance un scan complet d'un dossier (commande `scan`). Renvoie le rapport
     * texte humain-lisible (le format JSON est reserve a `analyze`).
     */
    async scan(rootPath: string): Promise<string> {
        const binary = this.resolveBinary();
        return new Promise<string>((resolve, reject) => {
            const proc = spawn(binary, ['scan', rootPath, '--no-color'], { cwd: rootPath });
            let buf = '';
            proc.stdout!.on('data', (d: Buffer) => { buf += d.toString('utf8'); });
            proc.stderr!.on('data', (d: Buffer) => { buf += d.toString('utf8'); });
            proc.on('error', reject);
            proc.on('close', code => {
                if (code === 2) reject(new Error(buf || 'aspx-lint scan failed'));
                else resolve(buf);
            });
        });
    }

    /**
     * Lance `aspx-lint fix <dir>` sur un dossier. La commande applique tous
     * les fixes auto-fixables. Pour des fixes selectifs, on pourra ajouter
     * `--rule <id>` en argument.
     */
    async fixDirectory(dirPath: string, ruleId?: string): Promise<string> {
        const binary = this.resolveBinary();
        const args = ['fix', dirPath];
        if (ruleId) args.push('--rule', ruleId);
        return new Promise<string>((resolve, reject) => {
            const proc = spawn(binary, args, { cwd: dirPath });
            let buf = '';
            proc.stdout!.on('data', (d: Buffer) => { buf += d.toString('utf8'); });
            proc.stderr!.on('data', (d: Buffer) => { buf += d.toString('utf8'); });
            proc.on('error', reject);
            proc.on('close', code => {
                if (code === 2) reject(new Error(buf || 'aspx-lint fix failed'));
                else resolve(buf);
            });
        });
    }
}
