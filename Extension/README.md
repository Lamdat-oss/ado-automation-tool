# ADO Automation Tool Extension

This folder contains the source for the Azure DevOps Extension.

## Structure

- `vss-extension.json`: The extension manifest.
- `index.html`: The Hub page displayed in Azure DevOps.
- `overview.md`: The description for the Marketplace listing.

## Prerequisites

1.  **TFX CLI**: Required to package the extension. Install with `npm install -g tfx-cli`.

## Packaging

1.  Navigate to the `Extension` folder.
2.  Run the packaging command:
    ```bash
    tfx extension create --manifest-globs vss-extension.json
    ```
    This will generate a `.vsix` file (e.g., `Lamdat.ado-automation-tool-0.0.1.vsix`).

## Publishing

1.  Go to the [Visual Studio Marketplace management page](https://marketplace.visualstudio.com/manage).
2.  Create a publisher (if you haven't already).
3.  Update the `publisher` field in `vss-extension.json` to match your publisher ID.
4.  Upload the `.vsix` file.
5.  Share the extension with your organization to test it.
