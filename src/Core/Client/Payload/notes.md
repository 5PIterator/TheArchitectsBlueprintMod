Payload data structure needs to be updated to be more mod resistant.

Issues:
 - Payload data structure is not mod resistant. It stores the raw tile data, along with the ids that are not consistent between sessions. For example, if the mod load order changes, the ids will result in different tiles being placed.
 - Payload data structure is not efficient. Currently a 500x500 payload takes up 213mb of memory.

Solution:
 - Add into metadata field that tracks the list of mods that were loaded when the payload was created. This list will be purely for reference or for quick lookup within metadata.
 - The actual payload will be completely handled by tmodloader's TagCompound. The tile identity will be determined by the FullName (ModName:InternalName).
 - Retire JSON as a medium for storing the payload. It will be replaced with a TagCompound.