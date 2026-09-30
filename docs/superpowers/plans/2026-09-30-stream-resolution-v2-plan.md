# Stream Resolution V2 Implementation Plan

1. Add failing tests for V2 preset values, backward-compatible settings, scale calculation, and native-rectangle mapping.
2. Extend RemoteStreamSettings with optional max stream dimensions and update Manager preset/custom controls.
3. Add a pure StreamResolutionPolicy for aspect-preserving scale calculation and conservative stream-to-native rectangle mapping.
4. Refactor ScreenCaster to resize before diff/JPEG, keep a previous stream-space frame, force full frame on dimension changes, and leave Original on the existing native path semantics.
5. Add five-second local stream diagnostics with deterministic unit tests.
6. Run Shared/Desktop/Manager/Installer tests in the existing Windows CI.
7. Run the repository Publish.ps1 package workflow and verify Agent/Desktop/Manager contents.
8. Hand the x64 package to the user for physical tests at Native, 1280x720, 960x540, and 640x360. Do not call physical E2E passed until those tests are reported.