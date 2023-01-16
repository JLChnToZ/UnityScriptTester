JLChnToZ Unity Editor Utilities
==================

This package formly named "Unity Script Tester Kit", but as I expands the tools within, it is not only for testing Unity behavior scripts.
Now it is a general multiple-purpose utility collection that contains some tools I used daily.

Features
-----
Inside the Script Tester Kit, it contains these components: **Inspector+**, **Selection Ex**, **Scene Manager**, **Player Prefs Editor**.

### Inspector+
This is an inspector-like panel looks like the built in inspector in Unity, but a bit more powerful than that. You can access every methods, properties, fields provided in the behavior (and non behaviours too) you selected with Inspector+, even they are invisible to global (marked as private or protected). It use reflection methods to access everything in the behaviors. It may be useful for digging in to the state and debugging.

To call this panel out, you can simply select *Window > JLChnToZ > Inspector+*.

### Selection Ex.
This is a selection management tool. It can temporary remembers, records, organizes objects you have selected, and restores selections later on.

To call this panel out, you can simply select *Window > JLChnToZ > Selection Ex*.

### Player Prefs Editor
The name is self explantory, this tools manages player preferences. But due to limitation, it don't lists all player prefs when you testing the editor, instead it tracks/watches what prefs you want it to manage.

To call this panel out, you can simply select *Window > JLChnToZ > Player Prefs*.

### Scene Manager
This is a scene manager that allows you directly jump to specific scene and test your project, and/or return to the scene you previous in when stopped. Also you can add/remove/manage the scenes to build directly in this window.

To call this panel out, you can simply select *Window > JLChnToZ > Scene Manager*.

Installation
-------
TBD for details. You may follow [this manual](https://docs.unity3d.com/Manual/upm-ui-giturl.html) to install this package.

License
-------
Everything in this repository is licensed with [MIT](LICENSE).