# UML Diagrams

This folder contains PlantUML diagrams generated from the current project-owned
Unity code under `Assets/`.

- `uml-usecase.puml` - actor/use-case diagram for player runtime flows and Unity editor tooling.
- `uml-class-diagram.puml` - package-oriented class diagram covering core services, episode runtime, RestScene UI, drag/drop, bartending, narrative graph data/runtime, and editor tools.

Notes:

- Unity, TMPro, UGUI, GraphView, and other package-cache framework classes are intentionally collapsed into external stereotypes or base classes.
- `Library/PackageCache` code is excluded; the diagram targets the project code and assets in this repository.
- The runtime story path is currently `EpisodeData` + `EpisodeRunner`. The `NarrativeGraphSO` pipeline is represented as an editor/baked-data path that can compile or bake into runtime data.
