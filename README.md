# VRTest


Contains tests and demos of a few of my VR experiments.

Open "VRTestUnity" as a Unity project.  Go to the asset store and
manually install these two extra assets: "SteamVR" and "BaroqueUI".


## ColorPicker

A simple VR color picker, done as a single solid object to interact with.
Allows to pick either a random color from the continuous spectrum, or snapping
on some colors.


## LibBVH

A "Bounding Volume Hierarchy" implementation.  Gives an efficient and flexible way
to locate which 3D objects are close to some place, given a large collection of
such 3D objects.  The 3D objects can be any shape you want; you need to describe
them as bounding boxes.  Similarly, the condition for intersection can be described
by any code you want.
