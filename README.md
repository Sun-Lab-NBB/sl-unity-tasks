# Gimbl-Tasks
A set of tasks for VR animal experiments in Unity using the GIMBL package.

## Detailed Description

This repository contains .unitypackage files which represent GIMBL tasks. A GIMBL task is a Unity prefab of a linear maze. After making a GIMBL Unity project, a GIMBL task can be imported, added to the scene, and run.

The original GIMBL repository can be found here:
https://github.com/winnubstj/Gimbl
___

## Features

- Supports Windows, Linux, and OSx.
- Compatible with GIMBL
- GPL 3 License.
___

## Table of Contents

- [Dependencies](#dependencies)
- [Installation](#installation)
- [Usage](#usage)
- [Developers](#developers)
- [Authors](#authors)
- [License](#license)
- [Acknowledgements](#Acknowledgments)
___

## Dependencies

See dependencies of https://github.com/winnubstj/Gimbl. No additional dependencies are required.
___

## Installation


Create a GIMBL Unity Project. Do this by following the instructions given in the __Quick Start__ portion of the GIMBL readme, again found here: https://github.com/winnubstj/Gimbl. Specifically, complete the __Import Gimbl into Unity__ and the __Setting up the Actor__ subsections.

Download the .unitypackage file corresponding to the task you wish to run.

Next, complete the __Setting up the task__ portion of the readme. Follow the same instructions in the readme but instead of importing InfiniteCorridorTask.unitypackage, import the downloaded .unitypackage file.

You should now see the prefab object in your assets folder. Just as the tutorial specifies, drag the prefab into the hierarchy window. The prefab will be within a file called "InfiniteCorrridorTask". Make sure you don't drag the prefab directly into the scene because then the task will have some offset. 

Assets tab with prefab:

  <img src="imgs/assets_tab.png" width="700">

Prefab:

  <img src="imgs/prefab.png" width="150">

Hierarchy Window before and after adding task:

  <img src="imgs/hierarchy_window_before.png" width="300">  <img src="imgs/hierarchy_window_after.png" width="300">

The last part of the tutorial involves setting the path of the controller. This may or may not be necessary depending on the specific task, see below.  

### Task Specific Instructions
* __IvanTask_1:__ This task has a tunnel path. Thus, after dragging the prefab into the hierarchy window, you need to set the path of the controller to TunnelPath and turn on the Loop Path parameter. These steps can be done from the Edit dropdown menu of the Controller panel, found in the Actors window. See the __Setting the path__ section from the tutorial.

* __ChelseaTask_1:__ This task does not have a tunnel path. Thus, leave path unspecified and the Loop Path parameter turned off. In order to connect the actor to the path, do the following:

  * In the hierarchy window, click on the task prefab.
  * Click on the Inspector tab.
  * You should see a C# script called Task. This script should have a parameter called Actor. Set the value of this parameter to the actor object

  Inspector Window before and after setting the actor object:

  <img src="imgs/chelseaTask1_before.png" width="300">  <img src="imgs/chelseaTask1_after.png" width="300">
  
  Instead of saying "Rod", it will say whatever you named your actor.
___

## Usage

After connecting the actor and the task, you can run the task by pressing the play button. To configure the task for experimental use, you will need to set some additional parameters:

  * Change the controller from a simulated linear treadmill to a linear treadmill.
  * Turn on the Must Lick parameter (see __Change the task parameters__ from the tutorial)
  * Turn off the Visible Marker parameter (see __Change the task parameters__ from the tutorial)

___


## Developers

To modify a task, use a Unity project as a workspace. 

* First follow the above installation instructions to install GIMBL in a Unity project and import the task you want to modify. 

* However, instead of dragging the prefab into the hierarchy window, just double cick on it. This will replace the hierarchy window for the scene with a hierarchy window for the prefab. You will know you are editing the prefab itself and not just a copy of it if the game view has a blue background:
 <img src="imgs/modifying_prefab.png" width="600">

* Make any changes to the structure of the maze by modifying this prefab. Make any changes to the control logic of the maze by modifying the Task script.

* Once you're done with the modifications, you can export the modified assets as a new Unity package:
  * In the Project window, select all the assets related to the modified version of the package. (select the InfiniteCorridorTask folder)
  * Right-click on the selected assets and choose Export Package.
  * In the Export Package window, ensure that Include Dependencies is checked (this ensures that all dependent files are included).
  * Choose a location and name for the package and save it to disk. It will be saved as a new .unitypackage
___

## Authors

- Jacob Groner ([Jgroner11](https://github.com/Jgroner11))
- Ivan Kondratyev ([Inkaros](https://github.com/Inkaros))

___

## License

This project is licensed under the GPL3 License: see the [LICENSE](LICENSE) file for details.
___

## Acknowledgments

- All Sun Lab [members](https://neuroai.github.io/sunlab/people) for providing the inspiration and comments during the
  development of this library.