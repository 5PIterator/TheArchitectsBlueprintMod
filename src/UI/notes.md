Make a plan for the new InterfaceSystem.

InterfaceSystem will replace the current OverlaySystem (Point, Border, etc will turn into UIPoint, UIBorder, etc). Instead of drawing GUI directly, it will let other systems register UIViews and their specific layers to be drawn every update. Similarly to MasterView, it will handle events such as hover, click, etc.
- InterfaceSystem's main functionality is to draw all UIViews in their respective layers.
- Secondary functionality is to house a database of all UIViews, Their respective layers, and their hitboxes in form of a rectangle. This will allow InterfaceSystem to run hit tests specific to each layer, and trigger UI events within the UIView.

ToolActionManager will be made completely redundant with the new event driven system.
Before:
- Item -> TAM -> BlueprintControl -> Blueprint

After:
- Item -> InterfaceSystem -> UIBlueprint

The ToolAction will remain as an Enumerate, acting as a Key for the relevant UIView. For example, if SET wand clicks on a Projection, The UIProjection will ignore the event since it requires a different toolAction.