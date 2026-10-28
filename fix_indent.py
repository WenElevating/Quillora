p = "D:\\GithubProject\\Quillora\\samples\\WpfMarkdownEditor.Sample\\MainWindow.xaml.cs"
with open(p, encoding="utf-8") as f:
    c = f.read()

# Fix the OnToggleIntegratedMode method indentation
old = (
    "\n\n    private void OnToggleIntegratedMode()\n"
    "    {\n"
    "        try\n"
    "        {\n"
    "            Editor.IntegratedMode = !Editor.IntegratedMode;\n"
    "            if (Editor.IntegratedMode)\n"
    "                Editor.IntegratedSource?.Focus();\n"
    "            else\n"
    "                Editor.Focus();\n"
    "        }\n"
    "        catch (Exception ex)\n"
    "        {\n"
    "            _viewModel.IsLoadingFile = false;\n"
    "            MessageBox.Show(\n"
    "                _localizationService.Format(\"Error.FileOperation\", ex.Message),\n"
    "                _localizationService.GetString(\"Common.Error\"),\n"
    "                MessageBoxButtons.OK,\n"
    "                MessageBoxImage.Error);\n"
    "        }\n"
    "    }\n"
)

new = (
    "\n\n    private void OnToggleIntegratedMode()\n"
    "    {\n"
    "        try\n"
    "        {\n"
    "            Editor.IntegratedMode = !Editor.IntegratedMode;\n"
    "            if (Editor.IntegratedMode)\n"
    "                Editor.IntegratedSource?.Focus();\n"
    "            else\n"
    "                Editor.Focus();\n"
    "        }\n"
    "        catch (Exception ex)\n"
    "        {\n"
    "            _viewModel.IsLoadingFile = false;\n"
    "            MessageBox.Show(\n"
    "                _localizationService.Format(\"Error.FileOperation\", ex.Message),\n"
    "                _localizationService.GetString(\"Common.Error\"),\n"
    "                MessageBoxButtons.OK,\n"
    "                MessageBoxImage.Error);\n"
    "        }\n"
    "    }\n"
)

if old in c:
    c = c.replace(old, new)
    with open(p, "w", encoding="utf-8") as f:
        f.write(c)
    print("Fixed")
else:
    print("Pattern not found")
    # Debug: show exact content
    lines = c.split('\n')
    for i in range(589, 612):
        print(f"{i+1}: [{lines[i]}]")