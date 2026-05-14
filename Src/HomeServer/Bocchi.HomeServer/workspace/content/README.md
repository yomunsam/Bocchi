# 内容空间（Content Space）

本目录是一个**独立的、可携带的"源工程"**，承载你的全部创作内容：
Blog、独立页面、作品集、短文、友链、站点设置等。

关键约定：

- 仅放原始 Markdown 与原始媒体；**禁止出现任何构建产物**（如 webp、缩略图、HTML、搜索索引）。
- 不包含任何 Theme 实现或 Theme 专有配置；那些属于 Bocchi 程序的实现细节。
- Post / Work / Note / Photo 一律使用年份目录作为一级分类。
- Post / Work 单篇为目录形式：`<kind>/<year>/<slug>/index.md` + `assets/`。
- Page 不按年份分类：`pages/<slug>/index.md`。
- Note 为单文件：`notes/<year>/<filename>.md`。
- frontmatter 一律 YAML（首尾 `---`）。

本目录可以独立用 Git 管理；将来 Bocchi 这个程序若被替换或弃用，
把本目录整体打包带走即可，不会丢失任何内容资产。