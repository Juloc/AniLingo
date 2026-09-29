import { mkdir, readdir, readFile, writeFile } from "node:fs/promises";
import { extname, join, relative, resolve } from "node:path";
import SVGSpriter from "svg-sprite";

const repositoryRoot = resolve(import.meta.dirname, "..");
const sourceRoot = join(repositoryRoot, "src", "Jularr.Web", "frontend", "assets", "icons");
const outputRoot = join(repositoryRoot, "src", "Jularr.Web", "wwwroot", "build");

async function listSvgFiles(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  const nested = await Promise.all(entries.map(async entry => {
    const path = join(directory, entry.name);
    if (entry.isDirectory()) {
      return listSvgFiles(path);
    }

    return extname(entry.name).toLowerCase() === ".svg" ? [path] : [];
  }));

  return nested.flat();
}

const sourceFiles = await listSvgFiles(sourceRoot);
if (sourceFiles.length === 0) {
  throw new Error("The icon source directory does not contain any SVG files.");
}

const sprite = new SVGSpriter({
  dest: outputRoot,
  shape: {
    id: {
      generator: name => `app-${relative(sourceRoot, name).replaceAll("\\", "-").replace(/\.svg$/i, "")}`
    }
  },
  mode: {
    symbol: {
      dest: ".",
      sprite: "icons.svg"
    }
  }
});

for (const sourceFile of sourceFiles) {
  sprite.add(sourceFile, null, await readFile(sourceFile, "utf8"));
}

const files = await new Promise((resolveBuild, rejectBuild) => {
  sprite.compile((error, result) => error ? rejectBuild(error) : resolveBuild(result));
});

await mkdir(outputRoot, { recursive: true });
for (const mode of Object.values(files)) {
  for (const resource of Object.values(mode)) {
    await mkdir(resolve(resource.path, ".."), { recursive: true });
    await writeFile(resource.path, resource.contents);
  }
}
