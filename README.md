# PhotoSorter
Yet another photo sorter!  This one is designed to identify and save off duplicate so they can be deleted.

PhotoSorter \<sourceFolder> [\<destFolder>] [-keepDuplicates]

If \<destFolder> is omitted, \<sourceFolder> is also used as \<destFolder>

PhotoSorter recursively processs the \<sourceFolder>.  For each JPEG in the current directory, it eads DateTimeOrginal and Model Exif tags. 
It then copies the JPEG to \<destFolder>\<dateTimeOriginal as yyyy-MM-dd>\<Model>.

if the -keepDuplicates flag is specified, duplicates are copies to \<destFolder>\<dateTimeOriginal as yyyy-MM-dd>\<Model>\<duplicates> as \<orginal file base> (\<n>).\<orginal file extension>,
where n is an int >= 1.

if the -keepDuplicates flag is omitted, duplicates are copies to \<destFolder>\<dateTimeOriginal as yyyy-MM-dd>\<Model> as \<orginal file base> (\<n>).\<orginal file extension>,
where n is an int >= 1 only if the duplicate has a different file checksum.  Otherwise duplicates are deleted.

If the original JPEG parent folder contained any information other than the date, a TXT file is written noting the orginal path.

To allow PhotoSorter to be run repeatedly where \<destFolder> = \<sourceFolder>, PhotoSorter wills ignore JPEG files already in the correct directory, and any duplicates directories.


