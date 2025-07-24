# sl-unity-tasks

A C# Unity project that provides assets to create and execute Virtual Reality (VR) tasks used to facilitate 
experiments in the Sun (NeuroAI) lab. 

---

## Detailed Description

This project provides assets and bindings for building Virtual Reality (VR) tasks used by some data acquisition systems
in the Sun lab to conduct experiments. Primarily, the project is designed to construct an **infinite linear corridor**
environment and display it to the animal during runtime using a set of three Virtual Reality monitors (screens).

This project is specialized to work with the main [sl-experiment](https://github.com/Sun-Lab-NBB/sl-experiment) library 
used by all Sun lab data acquisition systems. It uses [MQTT](https://mqtt.org/) to bidirectionally communicate with the 
sl-experiment runtimes and relies on sl-experiment to provide it with the data on animal's behavior during the VR task 
execution.

The project is an extension of the original [GIMBL](https://github.com/winnubstj/Gimbl) repository, heavily refactored 
to improve the flexibility of creating novel Unity VR tasks. In addition to the refactored GIMBL package, this project
exposes an interface for modifying existing and creating new tasks using prefabricated assets ('prefabs'). In addition 
to these changes, this project also deprecates most of the original GIMBL functionality no longer used in the Sun lab
(due to being replaced with sl-experiment), such as logging and unused MQTT topics. It also eliminates the technical 
debt left from the initial GIMBL development (unused assets, inefficient implementations, etc.).

___

## Features

- Runs on Windows, Linux, and OSx.
- Supports tasks with multiple virtual environment motifs (sub-corridors) and probabilistic transitions between these
  sub-motifs.
- GPL 3 License.

___

## Table of Contents

- [Dependencies](#dependencies)
- [Installation](#installation)
- [Usage](#usage)
- [Creating New Tasks](#creating-new-tasks)
- [Developer Notes](#developer-notes)
- [Authors](#authors)
- [License](#license)
- [Acknowledgements](#Acknowledgments)

___

## Dependencies

### Internal Dependencies

These dependencies are automatically installed with the project either as .dll files or as asset collections:
- [2MQTT package](https://github.com/eclipse/paho.mqtt.m2mqtt) version **4.3.0**.
- [Path-Creator package](https://github.com/SebLague/Path-Creator) latest available version.
- [SharpDX package](https://github.com/sharpdx/SharpDX/tree/master) version **4.2.0**.

### External Dependencies

The user must install these dependencies before working with this Unity project:
- [MQTT broker](https://mosquitto.org/) version **2.0.21**. This project was tested with the broker running locally, 
  using the **default** IP (27.0.0.1) and Port (1883) configuration.
- [Unity Game Engine](https://unity.com/products/unity-engine) version **6000.1.12f1**.
- [Blender](https://www.blender.org/download/) version **4.5.0 LTS**.

___

## Installation

### Source

1. Install the [Unity hub](https://unity.com/download) and use it to install the required Unity Game Engine version.
2. Download this repository to your local machine using your preferred method, such as Git-cloning. Use one
   of the stable releases from [GitHub](https://github.com/Sun-Lab-NBB/sl-unity-tasks/releases).
3. From the Unity Hub, select `add project from disk` and navigate to the local folder containing the downloaded
   repository: <br> <img src="imgs/AddProjectFromDisk.png" width="300"/>

**Hint.** If the correct Unity version is not installed when the project is imported, the Unity Hub displays a warning 
next to the project name. Click on the warning and install the recommended Unity version:
<br> <img src="imgs/InstallRecommendedVersion.png" width="600"/>

___

## Usage

This section discusses how to use existing tasks to conduct experiment and create new tasks using the project. 
**Note!** This library is specifically written to work with 
[sl-experiment](https://github.com/Sun-Lab-NBB/sl-experiment) library and will likely not work in other 
contexts.

### Creating New Tasks

The key feature of this project is the **task creator**: a system for quickly making any infinite corridor task with or 
without probabilistic transitions between corridor segments (trial motifs). 

#### Task Definition

Each **task** can be conceptualized as a set of infinite corridor **segments** and the **transition probabilities** 
between then. Each segment is split into **cues*, which are portions of the corridor walls that have different 
colors/textures. Since each task segment typically contains a reward zone that conditionally delivers water to the 
animal, traversing each segment typically constitutes a single **experiment trial**. Therefore, the sequence of wall 
cues that makes each segment is frequently referred to as the **trial motifs** in this project and the sl-experiment 
documentation.

Overall, a set of segments can represent any task graph depicting transitions between infinite corridor cues. For 
example, the cue graph below can be represented by two segments with uniform transition probabilities between 
each other:

<img src="imgs/cue_graph.png" width="233" alt="graph picture">

1. **Segment 1**: A, B, C
2. **Segment 2**: A, B, D, C

During experiment, both segments are typically reused many times to create a long sequence of segments to be experienced
by the animal during runtime.

In addition to the general task structure, there are additional parameters to be considered for each task, including:
- The length of each cue region.
- The length of non-cue ('gray') wall regions between the cue regions.
- The graphical texture (pattern) of each wall cue.
- The graphical texture of non-cue wall regions (usually gray color, hence the name **gray regions**).
- The graphical texture of the corridor floor.
- The reward zone locations and the conditions for the animal to receive the rewards.

#### Implementation

To create a task according to the desired specification, two assets need to be generated: a Unity prefab for each 
segment and the metadata .json file. The easiest way to create these assets is to start with an already existing 
task and modify it to match the desired parameters.

#### Segment Prefabs

All segment prefabs must be placed in the directory **Assets/InfiniteCorridorTask/Prefabs**. Double-clicking on a prefab 
opens up Unity's prefab editor. **Hint!** To verify that the file being edited is a prefab and not a GameObject, ensure
that the scene has a **blue** background.

<img src="imgs/segment_prefab.png" width="600">

Each prefab includes two key elements: the **reward location** and the **reset location**. For most tasks, the animal 
has to lick in the reward location to receive the reward. After successfully triggering a reward delivery, the mouse 
must pass through the reset location to get another reward.

Once each prefab segment is created, an additional prefab must be made for padding. This padding prefab should be a long
empty corridor, and it is used during task runtime to give the animal an illusion that the corridor is infinite.

#### Metadata JSON File
The **task metadata file**, also referred to as the **maze specification file**, ties the segment prefabs together and 
is requisite for creating and running tasks. The structure of this file is shown below and should be matched exactly for 
all custom metadata files for the project to work as intended:

- **cues** *(array\<Cue>)*: The list of all cues from any segment. The order of this list determines the integer id's 
  assigned to each cue during cue sequence logging.
  - **Cue**
    - **name** *(string)*: The unique human-readable label for the cue (e.g., `"A"`, `"Gray"`).  
    - **length** *(number, Unity units)*: The wall space, in unity units, taken up by the cue (the length of the cue).

- **segments** *(array\<Segment>)*: The list of all segments.
  - **Segment**
    - **name** *(string)*: The human-readable name of the segment prefab (e.g., `"Segment_abc"`). Must match the 
      prefab file name in **Assets/InfiniteCorridorTask/Prefabs**.
    - **cue_sequence** *(string[])*: The ordered list of cues in the segment. The sum of cue lengths in this list must 
      match the total length of the segment prefab, in Unity units.
    - **transition_probabilities** *(number[])*: The ordered list of probabilities of choosing each possible successor 
      segment. All probabilities in the list must sum to 1. The length of this list must correspond to the total number 
      of segments. This field is optional; if unspecified, the task parser will assume uniform transitions.

- **padding** *(object)*: The Segment object with no wall cues.
  - **name** *(string)*: The name of the padding prefab in** Assets/InfiniteCorridorTask/Prefabs**.

- **corridor_spacing** *(number, meters or units)*: The distance inserted between consecutive corridors (groups of 
  segments) when laying them out in the world.

- **segments_per_corridor** *(integer)*: Specifies how many segments are concatenated to form one complete corridor. 
  Setting this to 3 is generally enough to give the illusion that the corridor is infinite.

**Note!** Below is an example of a well-written metadata file for one of the existing tasks:
<img src="imgs/maze_spec.png" width="400">


#### CreateTask Tab
Once the task specification file is created, it must be placed in the **Assets/InfiniteCorridorTask/Tasks** directory. 
To then create the task prefab, use the **CreateTask → New Task** command. This will open up a file window to select the
metadata .json file. Once the file is selected, a secondary prompt will open to name and save the prefab. Once created,
the prefab can be loaded and executed as any pre-created task that comes with the project (see below).

<img src="imgs/createTask.png" width="700">

### Loading Existing Tasks

Each distribution of the project contains all tasks currently used in the Sun lab. To use an existing task, open the
 Unity project and follow these steps:
1. Create a new scene by clicking File → New Scene. Instead of using the default scene template, select **ExperimentTemplate** as the template. **Note!** The Unity project opens the first time with an empty scene. If prompted, choose not to save this empty scene.
   <br> <img src="imgs/newScene.png" width="600">
1. Navigate to **Assets/InfiniteCorridorTask/Tasks**. This folder contains prefabricated Unity assets (prefabs) for 
   all tasks actively or formerly used to conduct experiments in the Sun lab. Drag the prefab for the desired task into 
   the hierarchy window and wait for it to be loaded into the scene. **Note!**  If you have Preferences > Scene View > 3D Placement Mode set to "World Origin", then dragging the prefab into the hierarchy window will automatically position the task correctly. 
   <br> <img src="imgs/hierarchy_window.png" width="800">
1. Select the task's **GameObject** in the **Hierarchy** window and view the **Inspector** window. The **Inspector** 
   window reveals the **Transform** component and the **Task** script. There are two things you must verify:
    - That the transform's position is (0, 0, 0). If it isn't, set it to (0, 0, 0).
    - That the **Actor** parameter is set. If it is None, use the dropdown menu to set it to the **Actor Object** in the scene.

    The *Task* script contains additional parameters which should not need to be modified:
    - **Must Lick**: Determines whether the animal has to lick within the reward zone to get the reward. If disabled, 
      the animal gets the reward by entering the reward region and colliding with the invisible reward boundary wall.
      **Note!** During sl-experiment runtimes, this parameter is automatically overridden by the sl-experiment GUI and
      runtime logic, so setting the parameter in Unity will likely be ignored at runtime.
    - **Visible Marker**: Determines whether to reveal the typically hidden reward zone collision boundary to the 
      animal. **Note!** During sl-experiment runtimes, this parameter is automatically overridden by the sl-experiment 
      GUI and runtime logic, so setting the parameter in Unity will likely be ignored at runtime.
    - **Track Length**: The length of the track's wall cue sequence, in Unity units, to pre-create before runtime. 
      This is most relevant for tasks with multiple wall cue motifs and random transitions between these motifs.
      Pre-creating the cue sequence before runtime allows sl-experiment to accurately track transitions between trials
      and support trial-specific logic while treating the experiment runtime as a monolithic sequence of trials. 
      **Note!** If the animal traverses the entire pregenerated track, the Unity task starts making on the fly decisions
      about which trial motif the animal enters at the end of each trial. Likely, this will cause sl-experiment to abort
      with an error, as it is not notified of these additional trials. Therefore, **it is advised to pre-generate a 
      long cue sequence at each runtime, guaranteeing the animal is not able to fully traverse it at runtime**.
    - **Track seed**: The seed to use for resolving random transitions between trial motifs. This is helpful when 
      running many experiments with the exact same pattern of trial motif (segment) transitions. If set to -1, then no 
      seed is used and transitions are randomized at each task runtime. 
    - **Meta_data_path**: The file path to the metadata json file associated with the task. **Note!** If you move the 
    metadata json file to a new location in the project, this path will be invalid and the game will break. To fix this, 
    manually change this field to use the correct path (relative to local root) or recreate the task. See the 
    ['creating new tasks'](#creating-new-tasks) section for more details about this file.
1. Select File > Save As to save the scene in *Assets/Scenes*.
1. Click on the **DisplaysWindow** tab located to the right of the Inspector tab. If you don't see this tab, you can reopen it by clicking on Window > Gimbl. Press `Refresh Monitor Positions`. After doing this, you should see a list of the monitors connected to the computer. Assign **Camera: LeftMonitor**, **Camera: RightMonitor**, and **Camera: CenterMonitor** to the corresponding monitors used for display to the mouse. To check that you have assigned the monitors correctly, press `Show Full-Screen Views`. For more information about configuring displays, look at the [original GIMBL repository](https://github.com/winnubstj/Gimbl?tab=readme-ov-file#setting-up-the-actor).
 **Warning!** Since rebooting the system frequently changes the Monitor output ports, it is strongly advised to **always** check the monitors before running experiment tasks.
   <br> <img src="imgs/display_tab.png" width="300">

1. Press the play button to run the VR task. Verify that there are no errors displayed in the console window after 
   starting (playing) the task. **Hint!** If there are errors, start debugging by looking at the **first** error 
   printed, which is likely the true error. Other errors are likely a result of running a broken game loop after the 
   first error. **Note!** The template environment is designed for experiments, where motion and licks should be sent over the MQTT protocol. If you want to test your maze manually, you need to to replace the linear controller with a simulated linear controller, see [Setting Up the Actor](https://github.com/winnubstj/Gimbl?tab=readme-ov-file#setting-up-the-actor) for instructions on how to do this. 

___

## Developer Notes

These notes are primarily directory to project developers and task creators.

* Be careful about modifying segment prefabs. Even after task creation, the task prefab relies on the existence of the 
  segment prefabs to run as expected. This means that if segment prefabs are modified later, it will also modify all 
  tasks using that prefab. To make small changes to many tasks, use the same segment prefab multiple times to 
  automatically synchronize the changes across all modified tasks. To modify one task without changing other tasks 
  that use the same prefab, make a new prefab that is a duplicate of the old one and modify the .json metadata files 
  accordingly.
* Most changes to the task structure can be implemented by modifying the segment prefabs. However, modifying a prefab 
  may invalidate all specification files using that prefab. The specification file contains a lot of information that 
  needs to match the exact state of each prefab, so it is a good practice to ensure the validity of all specification 
  files after modifying the prefab. Also, it is good practice to recreate the task from the specification file following
  prefab modification. If the newly created task uses the same name as the old task, it will replace the old task 
  prefab.
* The [Loading Existing Tasks](#loading-existing-tasks) section explains how to create a scene to hold the desired task.
  When running multiple experiments (using different tasks) from the same computer, it may be cumbersome to have 
  multiple Unity projects or to have one Unity project and switch the active task between experiments (within the same 
  scene). The best practice is to create a separate scene for each experiment as part of the same Unity project and
  switch between scenes by double-clicking on them. When starting a new experiment, open the desired scene and run the 
  task. **Note!** The display configurations are scene-specific, so displays must be reconfigured separately for each 
  scene.
* Be cautious when pushing and pulling code with GitHub. Merging branch conflicts is challenging with Unity and will 
  likely require changing one of the conflicting branches completely. Try to avoid merge conflicts and focus on making
  changes to assets (prefabs) while avoiding making large changes to the scene. Additionally, it is a good practice to 
  close the Unity project before pushing/pulling.
* The original GIMBL package was designed to log all non-brian-activity experiment data. Since this project is 
  explicitly designed to work with sl-experiment that now does all logging, **all Unity logging has been removed from
  this project**.
* The simulated linear treadmill feature has the option for 
  [button topics](https://github.com/winnubstj/Gimbl?tab=readme-ov-file#try-out-the-task). Button topics are displayed 
  in the **edit controller** window, but the ability to add a new button topic is currently unavailable. To test 
  reaction to lick events manually, add the correct button topic directly to the asset file for the specific controller. 
  To do so, navigate to **Assets/VRSettings/Controllers** and find the **.asset** file associated with the controller. 
  Open this file and replace

      buttonTopics: []

    with

      buttonTopics:
      - LickPort/
* For information on how to send MQTT messages to Unity, see 
  [here](https://github.com/winnubstj/Gimbl/wiki/Example-code-of-MQTT-subscribing-and-publishing).
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
- The creators of the original [GIMBL](https://github.com/winnubstj/Gimbl) package and all dependencies used by that
  package.