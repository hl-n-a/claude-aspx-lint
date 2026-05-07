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

export interface RuleMetadata {
    id: string;
    name: string;
    description: string;
    severity: 'error' | 'warning' | 'info';
    hasFix: boolean;
}

interface AnalyzeResponse {
    ext: string;
    issues: LintIssue[];
}

interface RulesResponse {
    rules: RuleMetadata[];
}

/**
 * Wrapper autour du binaire `aspx-lint` (CLI). Les frontends VSCode passent
 * par cette classe pour ne jamais traiter les details d'argv ou de parsing.
 *
 * Toutes les operations sont stateless et stdin/stdout-pipees : pas de
 * fichiers temp, pas de race condition avec le save, pas de touche au
 * disque sur les fichiers en cours d'edition.
 */
export class Linter {
    private rulesCache: Map<string, RuleMetadata> | null = null;

    constructor(private output: vscode.OutputChannel) {}

    private resolveBinary(): string {
        const cfg = vscode.workspace.getConfiguration('aspxLint');
        return cfg.get<string>('path', 'aspx-lint');
    }

    private workCwd(filePath: string): string {
        return vscode.workspace.workspaceFolders?.[0]?.uri.fsPath ?? path.dirname(filePath);
    }

    /**
     * Pipe `content` sur stdin de `aspx-lint <args>`, retourne (stdout, stderr,
     * exitCode). Centralise la logique de spawn + gestion d'erreur.
     */
    private exec(args: string[], stdin: string | null, cwd?: string): Promise<{ stdout: string; stderr: string; code: number }> {
        const binary = this.resolveBinary();
        return new Promise((resolve, reject) => {
            let proc: ReturnType<typeof spawn>;
            try {
                proc = spawn(binary, args, { cwd: cwd ?? process.cwd() });
            } catch (err: any) {
                return reject(new Error(
                    `Cannot launch '${binary}'. Install with: dotnet tool install -g aspx-lint`
                ));
            }

            let outBuf = '';
            let errBuf = '';
            proc.stdout!.on('data', (d: Buffer) => { outBuf += d.toString('utf8'); });
            proc.stderr!.on('data', (d: Buffer) => { errBuf += d.toString('utf8'); });
            proc.on('error', err => reject(new Error(
                `aspx-lint launch failed: ${err.message}. Is it installed (dotnet tool install -g aspx-lint) and in PATH?`
            )));
            proc.on('close', code => resolve({ stdout: outBuf, stderr: errBuf, code: code ?? 0 }));

            if (stdin !== null) {
                proc.stdin!.write(stdin, 'utf8');
            }
            proc.stdin!.end();
        });
    }

    /**
     * Analyse un buffer en memoire. Renvoie la liste d'issues.
     */
    async analyze(content: string, filePath: string): Promise<LintIssue[]> {
        const ext = path.extname(filePath).toLowerCase().replace('.', '') || 'aspx';
        const cwd = this.workCwd(filePath);
        const { stdout, stderr, code } = await this.exec(
            ['analyze', '--ext', ext, '--stdin'], content, cwd);

        // exit codes : 0 = clean, 1 = issues found, 2 = error.
        if (code === 2) {
            throw new Error(`aspx-lint error: ${stderr.trim() || 'unknown'}`);
        }
        try {
            const parsed = JSON.parse(stdout) as AnalyzeResponse;
            return parsed.issues ?? [];
        } catch (err: any) {
            this.output.appendLine(`Failed to parse analyze output: ${stdout.slice(0, 500)}`);
            throw new Error(`Cannot parse aspx-lint output: ${err.message}`);
        }
    }

    /**
     * Applique le fix d'une regle (ou tous les fixes auto-fixables si
     * ruleId est null) sur un buffer en memoire. Renvoie le contenu corrige.
     * Pas de touche au disque.
     */
    async fixBuffer(content: string, filePath: string, ruleId?: string): Promise<string> {
        const ext = path.extname(filePath).toLowerCase().replace('.', '') || 'aspx';
        const cwd = this.workCwd(filePath);
        const args = ['fix', '--stdin', '--ext', ext];
        if (ruleId) args.push('--rule', ruleId);
        const { stdout, stderr, code } = await this.exec(args, content, cwd);

        if (code === 2) {
            throw new Error(`aspx-lint error: ${stderr.trim() || 'unknown'}`);
        }
        if (code === 1) {
            // Regle inconnue / argument invalide.
            throw new Error(stderr.trim() || 'aspx-lint fix returned exit 1');
        }
        return stdout;
    }

    /**
     * Charge les metadonnees de toutes les regles depuis `aspx-lint rules`.
     * Cache en memoire (les regles ne changent pas a chaud). Utile pour les
     * hovers, la doc, et tout ce qui veut afficher la description complete.
     */
    async getRules(): Promise<Map<string, RuleMetadata>> {
        if (this.rulesCache) return this.rulesCache;
        try {
            const { stdout, code } = await this.exec(['rules'], null);
            if (code !== 0) return new Map();
            const parsed = JSON.parse(stdout) as RulesResponse;
            this.rulesCache = new Map(parsed.rules.map(r => [r.id, r]));
            return this.rulesCache;
        } catch (err: any) {
            this.output.appendLine(`Failed to load rules: ${err.message}`);
            return new Map();
        }
    }

    /**
     * Reinitialise le cache des regles. Appele apres une mise a jour du
     * binaire (peu probable a chaud mais utile pour les tests).
     */
    invalidateRulesCache() { this.rulesCache = null; }

    /**
     * Lance un scan complet d'un dossier (commande `scan`). Renvoie le rapport
     * texte humain-lisible.
     */
    async scan(rootPath: string): Promise<string> {
        const { stdout, stderr, code } = await this.exec(
            ['scan', rootPath, '--no-color'], null, rootPath);
        if (code === 2) throw new Error(stderr.trim() || 'aspx-lint scan failed');
        return stdout + (stderr ? '\n' + stderr : '');
    }
}
