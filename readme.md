# Transform Cacher

A BepInEx plugin that allows tracking, saving, and exporting transforms in Unity games.

## Features

- Track and persist object transforms across game sessions
- Identify and tag objects in the game world
- Export models with textures to glTF/GLB format
- Load and spawn prefabs from asset bundles
- Simple, intuitive UI for managing objects

## Installation

1. Install [BepInEx](https://github.com/BepInEx/BepInEx) if you haven't already
2. Extract the TransformCacher folder into your BepInEx/plugins directory
3. Make sure all required DLLs are in the plugin folder:
   - AssetStudio.dll
   - GLTFSerialization.dll
   - K4os.Compression.LZ4.dll
   - System.Buffers.dll
   - System.Memory.dll
   - System.Numerics.Vectors.dll
   - Unity.RenderPipelines.ShaderGraph.ShaderGraphLibrary.dll
   - UnityGLTF.Helpers.dll
   - UnityGLTF.Plugins.Experimental.dll
   - UnityGltf.RenderPipelines.dll
   - UnityGLTFScripts.dll
   - ZstdSharp.dll
4. Launch the game

## Directory Structure

```
BepInEx/
└── plugins/
    └── TransformCacher/
        ├── TransformCacher.dll
        ├── [other required DLLs]
        ├── bundles/
        │   ├── unitygltf
        │   └── unitygltf.manifest
        └── Exports/
            └── [exported models go here]
```

## Usage

### Hotkeys

- **Ctrl+E**: Toggle the export window
- **F9**: Save all tagged objects
- **F10**: Tag the currently inspected object
- **Delete**: Mark the currently inspected object for destruction
- **F8**: Open the prefab selector
- **Alt+Tab**: Toggle between mouse UI control and game control

### Exporting Models

1. Select an object in the game world using Unity Explorer or the game's built-in selection mechanism
2. Press Ctrl+E to open the export window
3. Configure export settings (include children, GLB/GLTF format, filename)
4. Click "Export Selected Objects"
5. The model will be exported to `BepInEx/plugins/TransformCacher/Exports/<scene_name>/`

### Tagging Objects

1. Select an object in the game world
2. Press F10 to tag it
3. Press F9 to save all tagged objects

## Configuration

The plugin settings can be configured in the BepInEx configuration file:

```
[General]
EnablePersistence = true
EnableDebugGUI = true
EnableObjectHighlight = true

[Advanced]
TransformDelay = 2.0
MaxRetries = 3

[Exporter]
ExportDirectory = "path/to/export/directory"
OverwriteTextureFiles = true
AllowLowTextures = false
```

## Troubleshooting

- **Objects not exporting properly**: Make sure the object's mesh is readable. The plugin will attempt to reimport unreadable meshes, but this may not always work.
- **Missing textures**: Check if the texture files exist in the game's asset bundles. The plugin can only export textures that are accessible at runtime.
- **Incorrect model scale/orientation**: This is a limitation of the glTF format. Try adjusting the model in a 3D editor after export.

## Credits

This plugin uses the following libraries:
- AssetStudio for asset extraction
- UnityGLTF for model export

## License

CC0 1.0 Universal
