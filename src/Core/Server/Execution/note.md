Make a plan for the following changes:

I want to modify the current Prepare, Clear, Cut, Place, Sweep systems to be more modular.

Currently, there is only Prepare, Clear, Cut, Place, and they are all always run in order. I want to be able to toggle each of them on and off, which should natively support various pasting strategies. Additionally, Sweep should be moved into its own phase instead of being a part of the layer system. Also, rename Prepare to Plan

Plan: Runs the planning phase to put together the queues of other phases.
    - When enabled, makes the whole process more efficient by for example skipping clearing and placing tiles that are already in the correct position, and scheduling Cut/Paste.
    - If Plan is disabled, other phases should still run, but blindly if they can (WIP). For example, Sweep should still be able to run if Plan is disabled only for all tiles.
Clear: When enabled, clears the tiles that are in the clear queue. Handles various clearing strategies that further modify the planned queue:
    - Clear All (all target tiles), Transparent (only target tiles that overlap with source tiles), Negative (only target tiles that do not overlap with source tiles).
Cut: When enabled, cuts the tiles that are in the cut queue.
Place: When enabled, places the tiles that are in the place queue. Handles various placing strategies that further modify the planned queue:
    - Place (Simply try to place the tiles), Replace (Use ReplaceTile instead of PlaceTile to try to place the tiles)
Sweep: Sweeps the tiles that are in the sweep queue.
    - If Plan is enabled, Sweep will run for only succesfully placed tiles.
    - If Plan is disabled, Sweep will run for all tiles.