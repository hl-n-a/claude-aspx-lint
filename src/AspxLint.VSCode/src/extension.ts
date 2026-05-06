import * as vscode from 'vscode';
import { Linter, LintIssue } from './linter';

let diagnosticCollection: vscode.DiagnosticCollection;
let outputChannel: vscode.OutputChannel;
let linter: Linter;
let lintTimers: Map<string, NodeJS.Timeout> = new Map();

const SUPPORTED_EXTS = new Set(['.aspx', '.ascx', '.master', '.asax', '.config']);

export function activate(context: vscode.ExtensionContext) {
    outputChannel = vscode.window.createOutputChannel('aspx-lint');
    diagnosticCollection = vscode.languages.createDiagnosticCollection('aspx-lint');
    linter = new Linter(outputChannel);

    context.subscriptions.push(diagnosticCollection, outputChannel);

    // Lint on save (default behavior).
    context.subscriptions.push(
        vscode.workspace.onDidSaveTextDocument(doc => {
            if (getConfig().lintOnSave && isSupported(doc)) {
                runLint(doc);
            }
        })
    );

    // Lint on open.
    context.subscriptions.push(
        vscode.workspace.onDidOpenTextDocument(doc => {
            if (isSupported(doc)) runLint(doc);
        })
    );

    // Lint on type (debounced, opt-in).
    context.subscriptions.push(
        vscode.workspace.onDidChangeTextDocument(e => {
            if (!getConfig().lintOnType) return;
            if (!isSupported(e.document)) return;
            const key = e.document.uri.toString();
            const existing = lintTimers.get(key);
            if (existing) clearTimeout(existing);
            lintTimers.set(key, setTimeout(() => {
                lintTimers.delete(key);
                runLint(e.document);
            }, 500));
        })
    );

    // Clear diagnostics when a doc is closed.
    context.subscriptions.push(
        vscode.workspace.onDidCloseTextDocument(doc => {
            diagnosticCollection.delete(doc.uri);
        })
    );

    // Lint already-open editors at activation.
    vscode.workspace.textDocuments.forEach(doc => {
        if (isSupported(doc)) runLint(doc);
    });

    // Code actions : "Apply auto-fix" pour les regles fixables.
    context.subscriptions.push(
        vscode.languages.registerCodeActionsProvider(
            { scheme: 'file', pattern: '**/*.{aspx,ascx,master,asax,config}' },
            new AspxLintCodeActionProvider(),
            { providedCodeActionKinds: [vscode.CodeActionKind.QuickFix] }
        )
    );

    // Commandes manuelles.
    context.subscriptions.push(
        vscode.commands.registerCommand('aspxLint.scanWorkspace', async () => {
            const folder = vscode.workspace.workspaceFolders?.[0];
            if (!folder) {
                vscode.window.showWarningMessage('aspx-lint : aucun workspace ouvert.');
                return;
            }
            outputChannel.show(true);
            outputChannel.appendLine(`Scan workspace: ${folder.uri.fsPath}`);
            try {
                const result = await linter.scan(folder.uri.fsPath);
                outputChannel.append(result);
            } catch (err) {
                outputChannel.appendLine(`Erreur : ${err}`);
            }
        }),
        vscode.commands.registerCommand('aspxLint.fixCurrent', async () => {
            const editor = vscode.window.activeTextEditor;
            if (!editor) return;
            if (!isSupported(editor.document)) {
                vscode.window.showInformationMessage('aspx-lint : ce fichier n\'est pas un fichier ASPX/ASCX/MASTER/ASAX/Web.config.');
                return;
            }
            const path = editor.document.uri.fsPath;
            try {
                await editor.document.save();
                const dir = require('path').dirname(path);
                await linter.fixDirectory(dir);
                vscode.window.showInformationMessage('aspx-lint : auto-fix applique. Refresh recommande.');
                runLint(editor.document);
            } catch (err) {
                vscode.window.showErrorMessage(`aspx-lint fix : ${err}`);
            }
        }),
        vscode.commands.registerCommand('aspxLint.showOutput', () => {
            outputChannel.show(true);
        })
    );

    outputChannel.appendLine('aspx-lint : extension active.');
}

export function deactivate() {
    if (diagnosticCollection) diagnosticCollection.clear();
    lintTimers.forEach(t => clearTimeout(t));
    lintTimers.clear();
}

function getConfig() {
    const cfg = vscode.workspace.getConfiguration('aspxLint');
    return {
        path: cfg.get<string>('path', 'aspx-lint'),
        lintOnSave: cfg.get<boolean>('lintOnSave', true),
        lintOnType: cfg.get<boolean>('lintOnType', false),
        severityLevel: cfg.get<string>('severityLevel', 'info')
    };
}

function isSupported(doc: vscode.TextDocument): boolean {
    if (doc.uri.scheme !== 'file') return false;
    const path = require('path');
    const ext = path.extname(doc.uri.fsPath).toLowerCase();
    return SUPPORTED_EXTS.has(ext);
}

async function runLint(doc: vscode.TextDocument) {
    if (!isSupported(doc)) return;
    try {
        const issues = await linter.analyze(doc.getText(), doc.uri.fsPath);
        const diagnostics = issuesToDiagnostics(doc, issues, getConfig().severityLevel);
        diagnosticCollection.set(doc.uri, diagnostics);
    } catch (err: any) {
        outputChannel.appendLine(`Erreur de lint : ${err?.message ?? err}`);
        // Erreur silencieuse cote UI : on ne spamme pas l'utilisateur.
    }
}

function issuesToDiagnostics(
    doc: vscode.TextDocument,
    issues: LintIssue[],
    minSeverity: string
): vscode.Diagnostic[] {
    const order = { error: 0, warning: 1, info: 2 };
    const min = order[minSeverity as keyof typeof order] ?? 2;

    return issues
        .filter(i => (order[i.severity] ?? 2) <= min)
        .map(issue => {
            // Ligne 1-based dans aspx-lint, 0-based dans VSCode.
            const line = Math.max(0, issue.line - 1);
            const col = Math.max(0, issue.col - 1);
            const range = new vscode.Range(line, col, line, col + (issue.snippet?.length ?? 1));
            const sev =
                issue.severity === 'error' ? vscode.DiagnosticSeverity.Error :
                issue.severity === 'warning' ? vscode.DiagnosticSeverity.Warning :
                vscode.DiagnosticSeverity.Information;
            const diag = new vscode.Diagnostic(range, `${issue.ruleName} — ${issue.hint}`, sev);
            diag.code = issue.ruleId;
            diag.source = 'aspx-lint';
            return diag;
        });
}

/**
 * Provider qui propose un quick fix "Apply auto-fix" sur les regles fixables.
 * On ne sait pas cote VSCode si une regle est fixable, donc on propose toujours
 * — l'extension delegue a `aspx-lint fix --rule X` qui no-op si non-fixable.
 */
class AspxLintCodeActionProvider implements vscode.CodeActionProvider {
    provideCodeActions(
        _document: vscode.TextDocument,
        _range: vscode.Range,
        context: vscode.CodeActionContext
    ): vscode.CodeAction[] {
        const actions: vscode.CodeAction[] = [];
        for (const diag of context.diagnostics) {
            if (diag.source !== 'aspx-lint') continue;
            const ruleId = String(diag.code ?? '');
            if (!ruleId) continue;

            const action = new vscode.CodeAction(
                `aspx-lint : appliquer le fix de ${ruleId}`,
                vscode.CodeActionKind.QuickFix
            );
            action.command = {
                command: 'aspxLint.fixCurrent',
                title: 'aspx-lint fix',
                arguments: [ruleId]
            };
            action.diagnostics = [diag];
            actions.push(action);
        }
        return actions;
    }
}
