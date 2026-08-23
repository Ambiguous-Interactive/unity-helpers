# Skill: Editor Interaction Patterns

<!-- trigger: progress bar, undo, drag and drop, editor dialog | Editor UI mechanics - progress, undo, dialogs, drag-drop | Core -->

**Trigger**: When an editor tool runs a long operation, mutates assets or scene objects, refreshes
the `AssetDatabase`, accepts a drop, or asks the user a question. These are the interaction
mechanics that surround editor work; the caching that makes the work cheap is in
[editor-caching-patterns](./editor-caching-patterns.md).

---

## Progress Bar for Long Operations

```csharp
private void ProcessAssets(List<string> assetPaths)
{
    int total = assetPaths.Count;
    try
    {
        for (int i = 0; i < total; i++)
        {
            string path = assetPaths[i];

            if (EditorUtility.DisplayCancelableProgressBar(
                "Processing Assets",
                $"Processing {Path.GetFileName(path)} ({i + 1}/{total})",
                (float)i / total))
            {
                break; // User cancelled
            }

            ProcessSingleAsset(path);
        }
    }
    finally
    {
        EditorUtility.ClearProgressBar();
    }
}
```

## Undo Support

```csharp
// Record object before modification
Undo.RecordObject(targetObject, "Descriptive Undo Name");
targetObject.someField = newValue;

// For multiple objects
Undo.RecordObjects(serializedObject.targetObjects, "Change Multiple");

// Group multiple operations
Undo.SetCurrentGroupName("Complex Operation");
int undoGroup = Undo.GetCurrentGroup();
// ... multiple changes ...
Undo.CollapseUndoOperations(undoGroup);

// Register newly created objects
GameObject newObj = new GameObject("Created Object");
Undo.RegisterCreatedObjectUndo(newObj, "Create Object");
```

## AssetDatabase Refresh

```csharp
// After modifying assets on disk
AssetDatabase.Refresh();

// With import options
AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

// Import specific asset
AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

// Save all assets
AssetDatabase.SaveAssets();
```

## Drag and Drop

```csharp
private void HandleDragAndDrop(Rect dropArea)
{
    Event evt = Event.current;

    switch (evt.type)
    {
        case EventType.DragUpdated:
        case EventType.DragPerform:
        {
            if (!dropArea.Contains(evt.mousePosition))
            {
                return;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();

                foreach (Object draggedObject in DragAndDrop.objectReferences)
                {
                    string path = AssetDatabase.GetAssetPath(draggedObject);
                    // Process dropped object
                }
            }

            evt.Use();
            break;
        }
    }
}
```

## User Dialogs (with Suppression)

```csharp
private bool ConfirmAction(string message)
{
    if (SuppressUserPrompts)
    {
        return true; // Auto-confirm in batch/test mode
    }

    return EditorUtility.DisplayDialog(
        "Confirm Action",
        message,
        "Yes",
        "No"
    );
}

private string SelectFolder()
{
    if (SuppressUserPrompts)
    {
        return null;
    }

    return EditorUtility.OpenFolderPanel("Select Folder", "Assets", "");
}
```

---

## Related Skills

- [editor-caching-patterns](./editor-caching-patterns.md) - Caching strategies for Editor code
- [create-editor-tool](./create-editor-tool.md) - EditorWindows and Custom Inspectors
- [editor-undo-complete](./editor-undo-complete.md) - The complete undo policy and its tiers
- [defensive-editor-programming](./defensive-editor-programming.md) - Editor edge cases
