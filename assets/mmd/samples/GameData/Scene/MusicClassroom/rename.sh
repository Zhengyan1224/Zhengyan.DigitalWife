#!/bin/bash

for file in *.dds; do
    # 检查是否存在 .dds 文件，防止没有匹配文件时报错
    [ -e "$file" ] || continue

    # 获取文件名（不含扩展名）并转为大写
    base=$(basename "$file" .dds | tr '[:lower:]' '[:upper:]')

    # 新的文件名（大写文件名 + .DDS）
    newfile="${base}.DDS"

    # 如果原文件和新文件名不同，则重命名
    if [ "$file" != "$newfile" ]; then
        mv -- "$file" "$newfile"
        echo "Renamed: $file -> $newfile"
    fi
done
