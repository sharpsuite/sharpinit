# sharpinit
A modular, high-performance, modern init daemon in C#.

sharpinit is not ready for production use!

## Things to be done

- [ ] Service manager (the first and foremost priority for now)
  - [x] Unit file loader
    - [x] Unit file parser
    - [x] Parametrized initialization
    - [x] Dependency builder
    - [x] Support for various dependency shorthands (.wants, .requires)
    - [x] Support for patching together unit files (.d, vendor control)
    - [x] Conditions
    - [x] Assertions
  - [ ] Process manager
    - [x] Start, stop and manage processes by targets, slices and services
      - [x] target units
      - [x] service units
      - [x] socket units
      - [x] slice units
      - [x] scope units
      - [ ] timer units
    - [x] Handle cgroups for processes
    - [ ] Namespace isolation features
    - [x] Adopt orphaned processes and reap them
    - [x] Socket activation
    - [x] D-Bus integration
- [ ] Journal daemon
  - [x] Redirect and receive stdout and stderr for StandardOutput/Error=journal services
  - [ ] Save and load journal to and from disk
- [x] udev/device management
  - [x] device units
  - [ ] automount units
  - [x] mount units
