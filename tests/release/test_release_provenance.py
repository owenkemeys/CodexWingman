import importlib.util,json,subprocess,tempfile,unittest
from pathlib import Path
spec=importlib.util.spec_from_file_location('provenance',Path(__file__).parents[2]/'tools/release_provenance.py')
p=importlib.util.module_from_spec(spec);spec.loader.exec_module(p)
class ReleaseContract(unittest.TestCase):
 def setUp(self):
  self.temp=tempfile.TemporaryDirectory();self.root=Path(self.temp.name)
  for args in [('init',),('config','user.name','Test'),('config','user.email','test@example.invalid')]:p.git(self.root,*args)
  (self.root/'source.txt').write_text('source');p.git(self.root,'add','.');p.git(self.root,'commit','-m','baseline')
 def tearDown(self):self.temp.cleanup()
 def receipt(self):
  r={'schema':'codexwingman.verification.v1',**p.source(self.root),'checks':['release-contract','helpers','core','windows-build']}
  p.write_json(p.receipt_path(self.root),r);return r
 def test_dirty_and_untracked_source_blocked(self):
  self.receipt();(self.root/'source.txt').write_text('changed')
  with self.assertRaises(ValueError):p.verified(self.root)
  p.git(self.root,'checkout','--','source.txt');(self.root/'unexpected.txt').write_text('untracked')
  with self.assertRaises(ValueError):p.source(self.root)
 def test_new_commit_invalidates_previous_test_receipt(self):
  self.receipt();p.git(self.root,'commit','--allow-empty','-m','new commit')
  with self.assertRaises(ValueError):p.verified(self.root)
 def test_incomplete_verification_blocked(self):
  r=self.receipt();r['checks']=['helpers'];p.write_json(p.receipt_path(self.root),r)
  with self.assertRaises(ValueError):p.verified(self.root)
 def test_exact_commit_passes(self):
  r=self.receipt();self.assertEqual(p.verified(self.root)['commit'],r['commit'])
 def test_package_tamper_and_extra_files_blocked(self):
  pkg=self.root/'package';pkg.mkdir();(pkg/'CodexWingman.exe').write_bytes(b'fixture')
  p.write_json(pkg/'release.json',{'schema':p.SCHEMA,'commit':'fixture','files':p.package_files(pkg)})
  self.assertEqual(p.verify_package(pkg)['files'],1)
  (pkg/'extra.txt').write_text('extra')
  with self.assertRaises(ValueError):p.verify_package(pkg)
  (pkg/'extra.txt').unlink();(pkg/'CodexWingman.exe').write_bytes(b'changed')
  with self.assertRaises(ValueError):p.verify_package(pkg)
 def test_wrong_origin_blocks_release(self):
  p.git(self.root,'remote','add','origin','https://example.invalid/other.git')
  with self.assertRaises(ValueError):p.require_remote_main(self.root,p.source(self.root)['commit'])
 def test_explicit_public_repository_and_invalid_urls(self):
  config=self.root/'release-repository.json'
  config.write_text(json.dumps({'repository':'https://github.com/example/Wingman'}))
  self.assertEqual(p.repository_url(self.root),'https://github.com/example/Wingman')
  for url in ['https://token@github.com/example/Wingman','https://example.invalid/repo','https://github.com/example/Wingman?token=secret']:
   config.write_text(json.dumps({'repository':url}))
   with self.assertRaises(ValueError):p.repository_url(self.root)
if __name__=='__main__':unittest.main()

class InstallContract(unittest.TestCase):
 @unittest.skipUnless(__import__('os').name=='nt','Windows installer contract')
 def test_corrupt_package_leaves_existing_installation_untouched(self):
  import shutil
  shell=shutil.which('pwsh') or shutil.which('powershell')
  self.assertIsNotNone(shell)
  root=Path(__file__).parents[2]
  with tempfile.TemporaryDirectory() as temp:
   package=Path(temp)/'package';package.mkdir()
   installed=Path(temp)/'installed';installed.mkdir()
   marker=installed/'CodexWingman.exe';marker.write_bytes(b'preserve installed version')
   result=subprocess.run([shell,'-NoProfile','-File',str(root/'scripts/Install-VerifiedWingman.ps1'),'-Package',str(package),'-Destination',str(installed)],capture_output=True,text=True)
   self.assertNotEqual(result.returncode,0)
   self.assertEqual(marker.read_bytes(),b'preserve installed version')
   self.assertEqual(sorted(p.name for p in Path(temp).iterdir()),['installed','package'])
