#include <assert.h>
#include <errno.h>
#include <grp.h>
#include <limits.h>
#include <signal.h>
#include <stdlib.h>
#include <stdio.h>
#include <string.h>
#include <sys/resource.h>
#include <sys/types.h>
#include <sys/wait.h>
#include <syslog.h>
#include <unistd.h>
#include <fcntl.h>
#include <poll.h>

typedef struct {
    int32_t stdin_fd;
    int32_t stdout_fd;
    int32_t stderr_fd;
    int32_t control_fd;
    int32_t semaphore_fd;
    int32_t uid;
    int32_t gid;
    char* binary;
    char* working_dir;
    char** envp;
    char** argv;
} forkhelper_t;

int interruptible_dup2(int fd1, int fd2)
{
    while (dup2(fd1, fd2) == -1)
    {
        if (errno == EINTR)
            continue;
        
        return errno;
    }
}

int augmented_fork(forkhelper_t a)
{
    int ret = fork();

    forkhelper_t* args = &a;

    if (ret == 0)
    {
        fcntl(args->control_fd, F_SETFD, FD_CLOEXEC);
        fcntl(args->semaphore_fd, F_SETFD, FD_CLOEXEC);

        struct pollfd *semaphore_wait_fds;

        semaphore_wait_fds = calloc(1, sizeof(struct pollfd));

        if (!semaphore_wait_fds)
            exit(204);

        semaphore_wait_fds[0].fd = args->semaphore_fd;
        semaphore_wait_fds[0].events = POLLIN;
        semaphore_wait_fds[0].revents = 0;

        poll(semaphore_wait_fds, 1, 1000);

        if ((semaphore_wait_fds[0].revents & POLLIN) != POLLIN)
        {
            exit(223);
        }

        dprintf(args->control_fd, " starting\n");

        dprintf(args->control_fd, "_setsid\n", args->stdin_fd);
        if (setsid() < 0)
        {
            dprintf(args->control_fd, "setsid:%d\n", errno);
            exit(220);
        }

        int dup2_err;

        dprintf(args->control_fd, "_dup2-stdin:%d\n", args->stdin_fd);
        dup2_err = interruptible_dup2(args->stdin_fd, 0);
        if (dup2_err != 0) { dprintf(args->control_fd, "dup2-stdin:%d\n", dup2_err); exit(208); }

        dprintf(args->control_fd, "_dup2-stdout:%d\n", args->stdout_fd);
        dup2_err = interruptible_dup2(args->stdout_fd, 1);
        if (dup2_err != 1) { dprintf(args->control_fd, "dup2-stdout:%d\n", dup2_err); exit(209); }

        dprintf(args->control_fd, "_dup2-stderr:%d\n", args->stderr_fd);
        dup2_err = interruptible_dup2(args->stderr_fd, 2);
        if (dup2_err != 2) { dprintf(args->control_fd, "dup2-stderr:%d\n", dup2_err); exit(222); }

        if (args->gid >= 0)
        {
            dprintf(args->control_fd, "_setgid:%d:%d\n", args->gid, getgid());
            if (setgid(args->gid) != 0)
            {
                dprintf(args->control_fd, "setgid:%d\n", errno);
                exit(216);
            }
        }

        if (args->uid >= 0)
        {
            dprintf(args->control_fd, "_setuid:%d:%d\n", args->uid, getgid());
            if (setuid(args->uid) != 0)
            {
                dprintf(args->control_fd, "setuid:%d\n", errno);
                exit(217);
            }
        }

        if (args->envp)
        {
            char** local_envp = args->envp;

            while (*local_envp)
            {
                char* env = *(local_envp++);
                char* env_val = strstr(env, "=");

                if (!env_val)
                    continue;

                if (strlen(env_val) <= 1)
                    continue;
                
                int key_size = (env_val - env);
                char* env_key = (char*)calloc(key_size + 1, sizeof(char));
                strncpy(env_key, env, key_size);

                env_val++;
                dprintf(args->control_fd, "_setenv:%s=%s\n", env_key, env_val);
                setenv(env_key, env_val, 1);
            }

            dprintf(args->control_fd, "_setenv:done\n");
        }

        if (args->working_dir)
        {
            if (chdir(args->working_dir) != 0)
            {
                dprintf(args->control_fd, "chdir:%s:%d\n", args->working_dir, errno);
                exit(200);
            }
        }

        dprintf(args->control_fd, "_exec:%s\n", args->binary);

        if (execv(args->binary, args->argv))
            dprintf(args->control_fd, "execv:%d\n", errno);

        exit(203);
    }

    return ret;
}