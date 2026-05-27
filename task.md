# Rhode and Schwartz SMW signal generator application

## Overview

this is an application that can control and monitor the SMW200 signal generator from Rhode and Schwartz. The purpose of the application is to configure the signal generator with a user-selected arbitrary waveform file and start transmitting the signal from a low drive level (-60dBm) to a high drive level 0dBm in 5dbm increments.

## User Requirements

- Provide the user a GUI to enter the IP address and port number of the device to connect to.
- Provide a button to load an arbitrary waveform into the signal generator.
- Start the RF output on the signal generator.
- Auto increment the drive level in 10dBm increments with a 10 second dwell time.
- Allow the user to stop the RF output at any given time.

## Developer requirements

- The application is to be written in C# using WPF and MVVM UI.
- The application is to use Microsoft dependency injection nuget package.
