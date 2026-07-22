const vscode = require('vscode');
const fs = require('fs');
const path = require('path');
const { LanguageClient, TransportKind } = require('vscode-languageclient/node');

let client;

async function activate(context) {
  const executable = resolveLanguageServerExecutable(context);
  const serverOptions = {
    run: { command: executable, args: ['lsp'], transport: TransportKind.stdio },
    debug: { command: executable, args: ['lsp'], transport: TransportKind.stdio }
  };
  const clientOptions = {
    documentSelector: [{ scheme: 'file', language: 'cx' }],
    synchronize: {
      fileEvents: vscode.workspace.createFileSystemWatcher('**/*.cx')
    }
  };

  client = new LanguageClient('cx', 'CX Language Server', serverOptions, clientOptions);
  await client.start();
}

function resolveLanguageServerExecutable(context) {
  const configured = vscode.workspace.getConfiguration('cx.languageServer').get('path', '').trim();
  if (configured && configured.toLowerCase() !== 'cx') {
    return configured;
  }

  const bundledExecutable = path.join(
    context.extensionPath,
    'server',
    process.platform === 'win32' ? 'Cx.Cli.exe' : 'Cx.Cli');
  if (fs.existsSync(bundledExecutable)) {
    return bundledExecutable;
  }

  if (process.platform === 'win32' && process.env.LOCALAPPDATA) {
    const installedExecutable = path.join(process.env.LOCALAPPDATA, 'cx', 'bin', 'Cx.Cli.exe');
    if (fs.existsSync(installedExecutable)) {
      return installedExecutable;
    }
  }

  return 'cx';
}

async function deactivate() {
  if (client) {
    await client.stop();
  }
}

module.exports = { activate, deactivate };
