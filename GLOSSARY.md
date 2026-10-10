# BlueCafe

A cafe design tool for arranging original furniture and designing interior areas.

## Language

**Cafe**:
The complete design being edited, including its overall bounds, rooms, furniture
and Interior choices. Only one cafe is open at a time.
_Avoid_: Room (when referring to the whole cafe)

**Room**:
A named, continuous polygon area without internal holes within a cafe, with
its own Interior assignments. A room
boundary does not automatically create a physical wall.
_Avoid_: Enclosure (when referring to a room area)

**Interior**:
The catalog category containing Flooring, Wallpaper and Background. Flooring and
Wallpaper can be assigned to rooms; Background applies to the whole cafe.

**Preset**:
A saved cafe design that can be previewed and opened for editing. Opening one
replaces the open cafe after unsaved work is handled.

**Asset path**:
The user-provided local location from which BlueCafe loads cafe assets. It contains a prepared BlueCafe asset pack.

**Asset pack**:
A prepared collection of a BlueCafe catalog, its referenced mesh data, textures,
thumbnail images and source metadata, supplied locally by the user.
_Avoid_: Raw game files
