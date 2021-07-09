all:
		gcc -c -fPIC libforkhelper.c
		gcc -shared -fPIC -o libforkhelper.so libforkhelper.o

.PHONY: clean

clean:
		rm -f libforkhelper.o libforkhelper.so