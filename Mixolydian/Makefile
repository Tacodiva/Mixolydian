
.PHONY: mod game patch run all

all: game mod patch run

mod:
	cd ../Test/Mod && dotnet build -c Release
	rm -rf Game/Mods/*
	cp ../Test/Mod/bin/Release/net7.0/Mod.dll Game/Mods/Mod.dll

game:
	cd ../Test/Game && dotnet build -c Release
	rm -rf Game/Orignal
	cp -r ../Test/Game/bin/Release/net7.0 Game/Orignal

patch:
	dotnet run

run:
	cd Game/Patched && dotnet Game.dll