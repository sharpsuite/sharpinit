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
  - [ ] Process manager
    - [x] Start, stop and manage processes by targets, slices and services
    - [ ] Handle cgroups and namespace isolation for processes
    - [x] Adopt orphaned processes and reap them
