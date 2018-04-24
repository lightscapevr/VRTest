# VRTest


Contains tests and demos of a few of my VR experiments (Armin Rigo).

Open "VRTestUnity" as a Unity project.  Go to the asset store and
manually install these two extra assets: "SteamVR" and "BaroqueUI".


## ColorPicker

A simple VR color picker, done as a single solid object to interact with.
Allows picking either a random color from the continuous spectrum, or snapping
on some colors.  For example, here we are picking the RGB color which is exactly
(0, 0, 128):

![ColorPicker1](Screenshots/ColorPicker1.jpg?raw=true "ColorPicker1")


## LibBVH

A "Bounding Volume Hierarchy" implementation.  Gives an efficient and flexible way
to locate which 3D objects are close to some place, given a large collection of
such 3D objects.  The 3D objects can be any shape you want; you need to describe
them as bounding boxes.  Similarly, the condition for intersection can be described
by any code you want.


## LibLargeSketch

A component that draws "sketches", where a sketch is a collection of 3D polygons
and edges (lines), generated from scripts.  Supports _very large_ sketches that don't
change too often, while giving the tools to apply such changes when needed.  The
polygons can use different materials.  There are a few special-purpose operations like
temporarily changing which material shows up where, for highlighting/fading some
parts; and hiding or changing the material for the edges.  You can also use several
LargeSketch gameobjects with increasing details, and activate/deactivate them
dynamically; the more detailed ones either replace the less detailed ones, or work
additively.
