# DocMe Hide Mesh Islands

Hides small parts of a VRChat avatar mesh without touching the model file: a highlight that sits over a new eye, a sticker, a decoration you never wanted. Nothing is lost, and everything can be brought back.

## Install

Add the DocMe Tools listing to the VRChat Creator Companion: https://docmevr.github.io/vpm, then add **DocMe Hide Mesh Islands** to your project. The DocMe Eye Installer depends on this package, so VCC installs it alongside.

## Use

1. Open **Tools > DocMe > Hide Mesh Islands** and pick the avatar (the selected one is offered).
2. Press **Select parts to hide** and click parts in the Scene view. Each click selects one connected piece, shown in red; a second click unselects it.
3. Press **Hide selected parts** (or Enter in the Scene view). The parts are hidden in one go.
4. **Undo last** takes back the last selection, or the last hidden part when nothing is selected. **Show all** brings everything back.

Hidden parts are folded away by a blendshape on a copy of the mesh, so the original mesh asset stays untouched. If another blendshape pulls a folded part back into view, turn on *Delete hidden parts instead of folding them* under Advanced.

The avatar remembers what was hidden. If the model file is reimported (after an edit in Blender, say), the hidden parts are rebuilt on the new mesh automatically; turn that off under Advanced if you prefer to do it by hand.

Generated meshes are written to `Assets/DocMe Generated/Meshes`.

## For tool authors

The package also carries the mesh library the DocMe Eye Installer is built on: island flood fill, scene picking, pick storage that survives a reimport, and the reimport watcher. A tool that builds on top of a hidden mesh registers an `IReapplyHandler` and, if it owns vertices a click must not hide, adds a function to `HideIslandsCore.ProtectedVertices`.

## How this was made

The code in this repository was written by AI (Claude, by Anthropic). The idea, the design, every feature, the testing on real avatars and the decisions on how it should behave are mine (DocMe). I planned it and directed the work; the AI typed it out. Bug reports and pull requests are welcome, and anything that gets merged goes through my own review.

## License

MIT. See `LICENSE.md`.
