# sharpinit
A modular, high-performance, modern init daemon in C#.

sharpinit is not ready for production use!

## Things to be done

- [ ] Service manager (the first and foremost priority for now)
  - [ ] Unit file loader
    - [x] Unit file parser
    - [x] Parametrized initialization
    - [x] Dependency builder
    - [x] Support for various dependency shorthands (.wants, .requires)
    - [ ] Support for patching together unit files (.d, vendor control)
    - [ ] Conditions
    - [ ] Assertions
  - [ ] Process manager
    - [ ] Start, stop and manage processes by targets, slices and services
      - [x] target units
      - [x] service units
      - [x] socket units
      - [ ] slice units
      - [ ] scope units
      - [ ] timer units
    - [ ] Handle cgroups and namespace isolation for processes
    - [x] Adopt orphaned processes and reap them
    - [x] Socket activation
    - [ ] D-Bus integration
- [ ] Journal daemon
  - [x] Redirect and receive stdout and stderr for StandardOutput/Error=journal services
  - [ ] Save and load journal to and from disk
- [ ] udev/device management
  - [ ] device units
  - [ ] automount units
  - [ ] mount units
