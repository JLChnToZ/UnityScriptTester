Unity Script Tester Kit
==================
Unity Script Tester Kit is a middle-level editor toolkit for testing Unity behavior scripts. It use reflection methods to access everything in the behaviors. It is compatible with Unity 4 and 5.

Features
-----
Inside the Script Tester Kit, it contains the main component, **Inspector+**.

### Inspector+
This is an inspector-like panel looks like the built in inspector in Unity, but a bit more powerful than that. You can access every methods, properties, fields provided in the behavior (and non behaviours too) you selected with Inspector+, even they are invisible to global (marked as private or protected). It may be useful for digging in to the state and debugging.

To call this panel out, you can simply select *Window > Inspector+*.

Warning
-------
As this toolkit is using reflection methods to fetch everything out, it is dangerous that improper use may cause your game or even the editor crashes. Thus use this at your own risk.

License
-------
Everything in this repository is licensed with [MIT](LICENSE).