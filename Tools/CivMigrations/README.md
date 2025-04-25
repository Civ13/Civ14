# Civ13 entity migration

On the json file in this folder, register all entities you create or add that have matching entities in Civ13. For example, if you create a coat called `GreenCoat` that exists in Civ13 as `/obj/clothing/coat/green`, add it to the json as:

`"/obj/clothing/coat/green": "GreenCoat"`

You can use an array if there are several equivalences:

`["/obj/clothing/coat/green","obj/clothing/coat/greenish"]: "GreenCoat"`

This will help us convert maps and such in the future.
