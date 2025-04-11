#include <stdlib.h>
#include <process.h>
#include <stdio.h>

int main(int argc_, char* argv_[])
{
  int res = spawnl( P_WAIT, "HELLO.EXE", NULL );
  printf("spawnl result: %i\n", res);
  return 0;
}
