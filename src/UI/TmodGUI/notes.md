I am not happy with the current design of the import and export dialog and how they function and am in need of an overhaul. This new window will be called "Payload Browser" and will be main user interface for browsing and selecting payloads from the local filesystem.

- I don't want the window to be modal. User should be able to move it around and interact with the game when not currently in focus.
- It should be very compact, and should not take up too much space on the screen.
- The window should handle both import and export. User will press a button on the window to select a selection or projection to work with, and then press a button to export or import.
- The main section of the window should be a grid of cards, holding the name, thumbnail and player name taken from the payload metadata. The rest of the metadata should be displayed as text attached to the cursor, when hovering the card.

Functionality:
- Select Blueprint: User can at any time click on any blueprint (projection or selection), and the window will automatically select the blueprint as the one they are working with (Should be possible to simply watch the topmost blueprint). The blueprint will be outlined with a border (Currently implemented), and a line that connects the nearest corner of the blueprint and the window's corner.
- Browser Panel: A grid of cards with up to 3 rows and 5 columns. The panel has two modes toggleable by a small button. Compact mode, displays only thumbnails in a grid of 5 columns. List mode, displays all cards in a list, with the thumbnail on the left, and project, player, size and date on the right.
- Blueprint Panel: A panel that displays the selected blueprint's payload and options for exporting or importing. This is also where the name of the project can be changed. The window will keep a copy of the blueprint's payload in memory.
- Import: Clicking on a card will subject the selected blueprint to a preview of the payload. If the window is closed, or a cancel button is pressed, the blueprint will be reverted to the original state. (Could be done by simply keeping a copy of the payload, and directly loading the imported payload into the blueprint for a preview.), the actual import will be done by pressing a button on the blueprint panel.
- Export: Clicking on a button on the blueprint panel will export the selected blueprint to a payload file. The file will be saved to the local filesystem in the Payloads folder. The file will be named after the project, and the file will contain the payload data. (Most will not be changed from current implementation)
-