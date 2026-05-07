import * as vscode from 'vscode';
import * as path from 'path';
import { Linter, LintIssue, RuleMetadata } from './linter';

let diagnosticCollection: vscode.DiagnosticCollection;
let outputChannel: vscode.OutputChannel;
let linter: Linter;
const lintTimers: Map<string, NodeJS.Timeout> = new Map();

const SUPPORTED_EXTS = new Set(['.aspx', '.ascx', '.master', '.asax', '.config']);

export function activate(context: vscode.ExtensionContext) {
    outputChannel = vscode.window.createOutputChannel('aspx-lint');
    diagnosticCollection = vscode.languages.createDiagnosticCollection('aspx-lint');
    linter = new Linter(outputChannel);

    context.subscriptions.push(diagnosticCollection, outputChannel);

    // Pre-charge les metadonnees des regles (utilise par le hover provider).
    // Pas await — l'activation reste rapide ; les hovers attendront le cache.
    linter.getRules().catch(err => outputChannel.appendLine(`Pre-load rules failed: ${err}`));

    // ============= Diagnostics =============
    context.subscriptions.push(
        vscode.workspace.onDidSaveTextDocument(doc => {
            if (getConfig().lintOnSave && isSupported(doc)) runLint(doc);
        }),
        vscode.workspace.onDidOpenTextDocument(doc => {
            if (isSupported(doc)) runLint(doc);
        }),
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
        }),
        vscode.workspace.onDidCloseTextDocument(doc => {
            diagnosticCollection.delete(doc.uri);
        })
    );

    // Lint already-open editors at activation.
    vscode.workspace.textDocuments.forEach(doc => {
        if (isSupported(doc)) runLint(doc);
    });

    // ============= Code actions =============
    context.subscriptions.push(
        vscode.languages.registerCodeActionsProvider(
            { scheme: 'file', pattern: '**/*.{aspx,ascx,master,asax,config}' },
            new AspxLintCodeActionProvider(),
            { providedCodeActionKinds: [vscode.CodeActionKind.QuickFix] }
        )
    );

    // ============= Hover provider (description complete des regles) =============
    context.subscriptions.push(
        vscode.languages.registerHoverProvider(
            { scheme: 'file', pattern: '**/*.{aspx,ascx,master,asax,config}' },
            new AspxLintHoverProvider()
        )
    );

    // ============= Format provider (format on save / Shift+Alt+F) =============
    context.subscriptions.push(
        vscode.languages.registerDocumentFormattingEditProvider(
            { scheme: 'file', pattern: '**/*.{aspx,ascx,master,asax,config}' },
            new AspxLintFormattingProvider()
        )
    );

    // ============= Commands =============
    context.subscriptions.push(
        vscode.commands.registerCommand('aspxLint.scanWorkspace', cmdScanWorkspace),
        vscode.commands.registerCommand('aspxLint.fixCurrent',    cmdFixCurrent),
        vscode.commands.registerCommand('aspxLint.fixOneRule',    cmdFixOneRule),
        vscode.commands.registerCommand('aspxLint.showOutput',    () => outputChannel.show(true))
    );

    outputChannel.appendLine('aspx-lint : extension active.');
}

export function deactivate() {
    if (diagnosticCollection) diagnosticCollection.clear();
    lintTimers.forEach(t => clearTimeout(t));
    lintTimers.clear();
}

// =====================================================================
// Helpers
// =====================================================================
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
        outputChannel.appendLine(`Lint error : ${err?.message ?? err}`);
        // Pas de popup : lint silencieux en cas d'erreur, l'output channel
        // capture la trace.
    }
}

function issuesToDiagnostics(
    doc: vscode.TextDocument,
    issues: LintIssue[],
    minSeverity: string
): vscode.Diagnostic[] {
    const order: Record<string, number> = { error: 0, warning: 1, info: 2 };
    const min = order[minSeverity] ?? 2;

    return issues
        .filter(i => (order[i.severity] ?? 2) <= min)
        .map(issue => {
            // Ligne 1-based dans aspx-lint, 0-based dans VSCode.
            const line = Math.max(0, issue.line - 1);
            const col = Math.max(0, issue.col - 1);
            const range = new vscode.Range(line, col, line, col + Math.max(1, issue.snippet?.length ?? 1));
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
 * Trouve une diagnostic aspx-lint a une position du document. Utilise par
 * le hover provider (afficher la description) et le code action provider
 * (proposer le fix de la bonne regle).
 */
function diagnosticsAt(doc: vscode.TextDocument, position: vscode.Position): vscode.Diagnostic[] {
    const all = vscode.languages.getDiagnostics(doc.uri);
    return all.filter(d =>
        d.source === 'aspx-lint' && d.range.contains(position)
    );
}

// =====================================================================
// Commandes
// =====================================================================
async function cmdScanWorkspace() {
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
    } catch (err: any) {
        outputChannel.appendLine(`Erreur : ${err?.message ?? err}`);
    }
}

/**
 * Applique tous les auto-fixes au buffer courant. Pas de touche aux autres
 * fichiers du workspace, pas de save avant que l'utilisateur ait approuve.
 * L'edit est applique comme un TextEdit que VSCode peut undoer (Ctrl+Z).
 */
async function cmdFixCurrent() {
    const editor = vscode.window.activeTextEditor;
    if (!editor || !isSupported(editor.document)) {
        vscode.window.showInformationMessage(
            'aspx-lint : ouvre un fichier ASPX/ASCX/MASTER/ASAX/Web.config pour utiliser fix.');
        return;
    }
    await applyFixOnBuffer(editor, /*ruleId*/ undefined);
}

/**
 * Variante : applique le fix d'UNE regle precise (appele par un code action
 * via arguments). Utilise par les quick fixes Ctrl+. dans l'editeur.
 */
async function cmdFixOneRule(ruleId: string) {
    const editor = vscode.window.activeTextEditor;
    if (!editor || !isSupported(editor.document)) return;
    await applyFixOnBuffer(editor, ruleId);
}

async function applyFixOnBuffer(editor: vscode.TextEditor, ruleId?: string) {
    const doc = editor.document;
    const before = doc.getText();
    let fixed: string;
    try {
        fixed = await linter.fixBuffer(before, doc.uri.fsPath, ruleId);
    } catch (err: any) {
        vscode.window.showErrorMessage(`aspx-lint fix : ${err?.message ?? err}`);
        return;
    }
    if (fixed === before) {
        vscode.window.setStatusBarMessage(
            ruleId
                ? `aspx-lint : aucun fix applicable pour ${ruleId}.`
                : 'aspx-lint : aucun fix applicable.',
            3000);
        return;
    }
    // Remplace tout le doc en un seul TextEdit -> undo en un Ctrl+Z.
    const fullRange = new vscode.Range(
        doc.positionAt(0),
        doc.positionAt(before.length)
    );
    await editor.edit(builder => builder.replace(fullRange, fixed));
    runLint(doc);
}

// =====================================================================
// Code action provider : un quick fix par diagnostic aspx-lint
// =====================================================================
class AspxLintCodeActionProvider implements vscode.CodeActionProvider {
    async provideCodeActions(
        _document: vscode.TextDocument,
        _range: vscode.Range,
        context: vscode.CodeActionContext
    ): Promise<vscode.CodeAction[]> {
        const rules = await linter.getRules();
        const actions: vscode.CodeAction[] = [];
        for (const diag of context.diagnostics) {
            if (diag.source !== 'aspx-lint') continue;
            const ruleId = String(diag.code ?? '');
            if (!ruleId) continue;

            const meta = rules.get(ruleId);
            if (meta && !meta.hasFix) continue;   // on ne propose pas de fix sur une regle non-fixable.

            const action = new vscode.CodeAction(
                `aspx-lint : appliquer le fix de ${ruleId}`,
                vscode.CodeActionKind.QuickFix
            );
            action.command = {
                command: 'aspxLint.fixOneRule',
                title: `aspx-lint fix ${ruleId}`,
                arguments: [ruleId]
            };
            action.diagnostics = [diag];
            actions.push(action);
        }
        return actions;
    }
}

// =====================================================================
// Hover provider : description complete de la regle quand on survole
// une diagnostic aspx-lint
// =====================================================================
class AspxLintHoverProvider implements vscode.HoverProvider {
    async provideHover(
        document: vscode.TextDocument,
        position: vscode.Position
    ): Promise<vscode.Hover | undefined> {
        const diags = diagnosticsAt(document, position);
        if (diags.length === 0) return undefined;

        const rules = await linter.getRules();
        const md = new vscode.MarkdownString('', /*supportThemeIcons*/ true);
        md.isTrusted = true;
        for (const diag of diags) {
            const ruleId = String(diag.code ?? '');
            const meta: RuleMetadata | undefined = rules.get(ruleId);
            if (meta) {
                const sev = meta.severity.toUpperCase();
                const fix = meta.hasFix ? '✓ auto-fixable' : 'manuel';
                md.appendMarkdown(`**${meta.id}** — ${meta.name}  \n`);
                md.appendMarkdown(`*${sev} · ${fix}*\n\n`);
                md.appendMarkdown(`${meta.description}\n\n`);
            } else {
                // Fallback : on affiche juste le hint.
                md.appendMarkdown(`**${ruleId}**  \n`);
                md.appendMarkdown(`${diag.message}\n\n`);
            }
        }
        return new vscode.Hover(md);
    }
}

// =====================================================================
// Formatting provider : Shift+Alt+F / "Format Document" applique tous
// les auto-fixes
// =====================================================================
class AspxLintFormattingProvider implements vscode.DocumentFormattingEditProvider {
    async provideDocumentFormattingEdits(
        document: vscode.TextDocument
    ): Promise<vscode.TextEdit[]> {
        const before = document.getText();
        let fixed: string;
        try {
            fixed = await linter.fixBuffer(before, document.uri.fsPath);
        } catch (err: any) {
            outputChannel.appendLine(`Format error : ${err?.message ?? err}`);
            return [];
        }
        if (fixed === before) return [];
        const fullRange = new vscode.Range(
            document.positionAt(0),
            document.positionAt(before.length)
        );
        return [vscode.TextEdit.replace(fullRange, fixed)];
    }
}
